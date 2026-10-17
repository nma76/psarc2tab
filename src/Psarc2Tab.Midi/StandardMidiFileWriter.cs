using System.Buffers.Binary;
using System.Text;
using Psarc2Tab.Application;
using Psarc2Tab.Domain;

namespace Psarc2Tab.Midi;

/// <summary>Writes one arrangement as a type-zero Standard MIDI File.</summary>
public sealed class StandardMidiFileWriter : IArrangementFormatWriter
{
    /// <summary>The MIDI ticks per quarter note in exported files.</summary>
    private const ushort TicksPerQuarterNote = 960;

    /// <summary>The fixed MIDI tempo, in microseconds per quarter note.</summary>
    private const int MicrosecondsPerQuarterNote = 500_000;

    /// <summary>The largest delta time encodable in a four-byte MIDI variable-length quantity.</summary>
    private const int MaximumVariableLengthQuantity = 0x0FFFFFFF;

    /// <summary>The maximum number of note events expanded into one MIDI track.</summary>
    private const int MaximumNoteCount = 1_000_000;

    /// <inheritdoc />
    public string FormatName => "midi";

    /// <summary>Writes an arrangement with note timing preserved in seconds at a fixed 120 BPM.</summary>
    /// <param name="song">The song that owns the track.</param>
    /// <param name="track">The selected arrangement.</param>
    /// <param name="destination">The writable destination stream.</param>
    /// <exception cref="ArgumentException">The track is not in the song or the stream is not writable.</exception>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="InvalidDataException">The arrangement contains unsupported pitches or too many notes.</exception>
    public void Write(Song song, Track track, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(song);
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
        {
            throw new ArgumentException("The MIDI destination stream must be writable.", nameof(destination));
        }

        if (!song.Tracks.Contains(track))
        {
            throw new ArgumentException("The selected track does not belong to the supplied song.", nameof(track));
        }

        var noteCount = track.Notes.Count + track.Chords.Sum(chord => chord.Notes.Count);
        if (noteCount > MaximumNoteCount)
        {
            throw new InvalidDataException($"The arrangement exceeds the supported {MaximumNoteCount} MIDI note limit.");
        }

        var events = CreateEvents(song, track);
        using var trackStream = new MemoryStream();
        WriteTrack(trackStream, events);
        WriteHeader(destination, checked((uint)trackStream.Length));
        trackStream.Position = 0;
        trackStream.CopyTo(destination);
    }

    /// <summary>Builds sorted metadata, note, and end-of-track events.</summary>
    /// <param name="song">The song containing the arrangement cent offset.</param>
    /// <param name="track">The arrangement to convert.</param>
    /// <returns>MIDI events ordered by absolute tick and event priority.</returns>
    private static IReadOnlyList<MidiTrackEvent> CreateEvents(Song song, Track track)
    {
        var events = new List<MidiTrackEvent>();
        events.Add(new MidiTrackEvent(0, 0, CreateTextMetaEvent(0x03, $"psarc2tab {track.Name}")));
        events.Add(new MidiTrackEvent(0, 1, [0xFF, 0x51, 0x03, 0x07, 0xA1, 0x20]));
        events.Add(new MidiTrackEvent(0, 2, [0xC0, GetProgramNumber(track.Name)]));
        if (song.CentOffsetCents != 0)
        {
            AddPitchBendEvents(events, song.CentOffsetCents);
        }

        foreach (var note in track.Notes)
        {
            var pitch = GetMidiPitch(track.Tuning, note.StringNumber, note.FretNumber);
            AddNoteEvents(events, note.Position, note.Duration, pitch);
        }

        foreach (var chord in track.Chords)
        {
            foreach (var note in chord.Notes)
            {
                var pitch = GetMidiPitch(track.Tuning, note.StringNumber, note.FretNumber);
                AddNoteEvents(events, chord.Position, chord.Duration, pitch);
            }
        }

        var lastTick = events.Max(item => item.Tick);
        events.Add(new MidiTrackEvent(lastTick, 10, [0xFF, 0x2F, 0x00]));
        return Array.AsReadOnly(events.OrderBy(item => item.Tick).ThenBy(item => item.Priority).ToArray());
    }

    /// <summary>Encodes global cents offset as a channel pitch bend and its sensitivity setting.</summary>
    /// <param name="events">The event list receiving the setup messages.</param>
    /// <param name="centOffset">The arrangement pitch offset in cents.</param>
    /// <exception cref="InvalidDataException">The offset exceeds the supported 127-semitone bend range.</exception>
    private static void AddPitchBendEvents(List<MidiTrackEvent> events, decimal centOffset)
    {
        if (centOffset is < -12_700 or > 12_700)
        {
            throw new InvalidDataException("The arrangement cent offset exceeds the supported MIDI pitch bend range.");
        }

        var absoluteOffset = decimal.Abs(centOffset);
        var bendRangeSemitones = Math.Max(2, (int)decimal.Ceiling(absoluteOffset / 100));
        events.Add(new MidiTrackEvent(0, 3, [0xB0, 0x65, 0x00]));
        events.Add(new MidiTrackEvent(0, 3, [0xB0, 0x64, 0x00]));
        events.Add(new MidiTrackEvent(0, 3, [0xB0, 0x06, (byte)bendRangeSemitones]));
        events.Add(new MidiTrackEvent(0, 3, [0xB0, 0x26, 0x00]));
        events.Add(new MidiTrackEvent(0, 3, [0xB0, 0x65, 0x7F]));
        events.Add(new MidiTrackEvent(0, 3, [0xB0, 0x64, 0x7F]));

        var bendDistance = centOffset >= 0 ? 8191m : 8192m;
        var bendValue = 8192 + (int)decimal.Round(centOffset / (bendRangeSemitones * 100m) * bendDistance,
            MidpointRounding.AwayFromZero);
        bendValue = Math.Clamp(bendValue, 0, 16_383);
        events.Add(new MidiTrackEvent(0, 4,
            [0xE0, (byte)(bendValue & 0x7F), (byte)((bendValue >> 7) & 0x7F)]));
    }

    /// <summary>Adds note-on and note-off events for one pitch.</summary>
    /// <param name="events">The event list receiving note messages.</param>
    /// <param name="position">The note start time.</param>
    /// <param name="duration">The note duration.</param>
    /// <param name="pitch">The MIDI pitch number.</param>
    private static void AddNoteEvents(List<MidiTrackEvent> events, TimeSpan position, TimeSpan duration, byte pitch)
    {
        var startTick = ToMidiTick(position.Ticks);
        var endTick = ToMidiTick((decimal)position.Ticks + duration.Ticks);
        if (endTick <= startTick)
        {
            endTick = checked(startTick + 1);
        }

        events.Add(new MidiTrackEvent(startTick, 6, [0x90, pitch, 100]));
        events.Add(new MidiTrackEvent(endTick, 5, [0x80, pitch, 0]));
    }

    /// <summary>Converts a string and fret position into a MIDI pitch.</summary>
    /// <param name="tuning">The arrangement's open-string MIDI pitches.</param>
    /// <param name="stringNumber">The one-based string index.</param>
    /// <param name="fretNumber">The fret number.</param>
    /// <returns>The MIDI pitch in the range 0 through 127.</returns>
    /// <exception cref="InvalidDataException">The fretted pitch is outside the MIDI note range.</exception>
    private static byte GetMidiPitch(GuitarTuning tuning, StringNumber stringNumber, FretNumber fretNumber)
    {
        var pitch = (long)tuning.OpenStringMidiPitches[stringNumber.Value - 1] + fretNumber.Value;
        if (pitch > 127)
        {
            throw new InvalidDataException("An arrangement note exceeds the MIDI pitch range.");
        }

        return (byte)pitch;
    }

    /// <summary>Maps supported arrangement names to General MIDI program numbers.</summary>
    /// <param name="instrumentName">The arrangement name.</param>
    /// <returns>Clean electric guitar or fingered electric bass program number.</returns>
    /// <exception cref="InvalidDataException">The arrangement name is not supported for MIDI output.</exception>
    private static byte GetProgramNumber(string instrumentName) => instrumentName.ToLowerInvariant() switch
    {
        "lead" or "rhythm" => 27,
        "bass" => 33,
        _ => throw new InvalidDataException($"The '{instrumentName}' arrangement has no MIDI instrument mapping.")
    };

    /// <summary>Converts 100-nanosecond time units to ticks at the fixed file tempo.</summary>
    /// <param name="timeSpanTicks">The timeline position in <see cref="TimeSpan.Ticks"/> units.</param>
    /// <returns>The nearest MIDI tick, rounded away from zero.</returns>
    private static long ToMidiTick(decimal timeSpanTicks)
    {
        var ticksPerSecond = TicksPerQuarterNote * 1_000_000m / MicrosecondsPerQuarterNote;
        return checked((long)decimal.Round(timeSpanTicks * ticksPerSecond / TimeSpan.TicksPerSecond,
            MidpointRounding.AwayFromZero));
    }

    /// <summary>Writes the MIDI header and the already-sized track header.</summary>
    /// <param name="destination">The destination stream.</param>
    /// <param name="trackLength">The encoded track length.</param>
    private static void WriteHeader(Stream destination, uint trackLength)
    {
        destination.Write("MThd"u8);
        WriteUInt32(destination, 6);
        WriteUInt16(destination, 0);
        WriteUInt16(destination, 1);
        WriteUInt16(destination, TicksPerQuarterNote);
        destination.Write("MTrk"u8);
        WriteUInt32(destination, trackLength);
    }

    /// <summary>Writes sorted track events with delta-time variable-length quantities.</summary>
    /// <param name="destination">The temporary track stream.</param>
    /// <param name="events">The events in tick order.</param>
    private static void WriteTrack(Stream destination, IReadOnlyList<MidiTrackEvent> events)
    {
        long previousTick = 0;
        foreach (var item in events)
        {
            var delta = item.Tick - previousTick;
            while (delta > MaximumVariableLengthQuantity)
            {
                WriteVariableLengthQuantity(destination, MaximumVariableLengthQuantity);
                destination.Write([0xFF, 0x7F, 0x00]);
                delta -= MaximumVariableLengthQuantity;
            }

            WriteVariableLengthQuantity(destination, checked((int)delta));
            destination.Write(item.Data);
            previousTick = item.Tick;
        }
    }

    /// <summary>Creates a UTF-8 text meta event.</summary>
    /// <param name="metaType">The MIDI text meta-event type.</param>
    /// <param name="text">The event text.</param>
    /// <returns>The encoded event bytes without a delta time.</returns>
    private static byte[] CreateTextMetaEvent(byte metaType, string text)
    {
        var textBytes = Encoding.UTF8.GetBytes(text);
        using var eventStream = new MemoryStream();
        eventStream.Write([0xFF, metaType]);
        WriteVariableLengthQuantity(eventStream, textBytes.Length);
        eventStream.Write(textBytes);
        return eventStream.ToArray();
    }

    /// <summary>Writes a four-byte big-endian unsigned integer.</summary>
    /// <param name="destination">The destination stream.</param>
    /// <param name="value">The value to write.</param>
    private static void WriteUInt32(Stream destination, uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        destination.Write(bytes);
    }

    /// <summary>Writes a two-byte big-endian unsigned integer.</summary>
    /// <param name="destination">The destination stream.</param>
    /// <param name="value">The value to write.</param>
    private static void WriteUInt16(Stream destination, ushort value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        destination.Write(bytes);
    }

    /// <summary>Writes a MIDI variable-length quantity in its shortest byte representation.</summary>
    /// <param name="destination">The destination stream.</param>
    /// <param name="value">A nonnegative value no greater than the MIDI four-byte limit.</param>
    /// <exception cref="ArgumentOutOfRangeException">The value is outside the MIDI encoding range.</exception>
    private static void WriteVariableLengthQuantity(Stream destination, int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        if (value > MaximumVariableLengthQuantity)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "The value exceeds the MIDI delta-time limit.");
        }

        Span<byte> bytes = stackalloc byte[4];
        var index = bytes.Length - 1;
        bytes[index] = (byte)(value & 0x7F);
        while ((value >>= 7) > 0)
        {
            bytes[--index] = (byte)((value & 0x7F) | 0x80);
        }

        destination.Write(bytes[index..]);
    }

    /// <summary>Stores an event's absolute tick, ordering priority, and MIDI bytes.</summary>
    /// <param name="Tick">The absolute event tick.</param>
    /// <param name="Priority">The order of simultaneous events.</param>
    /// <param name="Data">The event data without a delta time.</param>
    private sealed record MidiTrackEvent(long Tick, int Priority, byte[] Data);
}
