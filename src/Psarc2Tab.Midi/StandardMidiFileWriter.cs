using System.Buffers.Binary;
using System.Text;
using Psarc2Tab.Application;
using Psarc2Tab.Domain;

namespace Psarc2Tab.Midi;

/// <summary>Writes one arrangement as a type-zero Standard MIDI File.</summary>
public sealed class StandardMidiFileWriter : IMultiTrackArrangementFormatWriter
{
    /// <summary>Whether exports include a generated General MIDI percussion track.</summary>
    private readonly bool _includeDrums;

    /// <summary>Creates a MIDI writer with optional synthetic percussion.</summary>
    /// <param name="includeDrums">Whether to add a generic beat-based drum track.</param>
    public StandardMidiFileWriter(bool includeDrums = false) => _includeDrums = includeDrums;

    /// <summary>The MIDI ticks per quarter note in exported files.</summary>
    private const ushort TicksPerQuarterNote = 960;

    /// <summary>The fallback tempo used when the chart has fewer than two beat markers.</summary>
    private const int FallbackMicrosecondsPerQuarterNote = 500_000;

    /// <summary>The largest delta time encodable in a four-byte MIDI variable-length quantity.</summary>
    private const int MaximumVariableLengthQuantity = 0x0FFFFFFF;

    /// <summary>The maximum number of note events expanded into one MIDI track.</summary>
    private const int MaximumNoteCount = 1_000_000;

    /// <summary>The number of melodic MIDI channels, excluding the GM percussion channel.</summary>
    private const int MaximumArrangementTracks = 15;

    /// <inheritdoc />
    public string FormatName => "midi";

    /// <summary>Writes an arrangement with its source beat timing or a fixed 120 BPM fallback.</summary>
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

        var trackBytes = new List<byte[]>
        {
            CreateTrackBytes(CreateEvents(song, track, channel: 0, includeTempo: true,
                $"psarc2tab {track.Name}"))
        };
        if (_includeDrums)
        {
            trackBytes.Add(CreateTrackBytes(CreateDrumEvents(song)));
        }

        WriteFile(destination, 0, trackBytes);
    }

    /// <inheritdoc />
    public void WriteMultiple(IReadOnlyList<Song> songs, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(songs);
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
        {
            throw new ArgumentException("The MIDI destination stream must be writable.", nameof(destination));
        }

        if (songs.Count == 0 || songs.Any(song => song is null || song.Tracks.Count != 1))
        {
            throw new ArgumentException("Combined MIDI export requires one or more single-track arrangements.", nameof(songs));
        }

        if (songs.Count > MaximumArrangementTracks)
        {
            throw new InvalidDataException($"Combined MIDI export supports at most {MaximumArrangementTracks} arrangements because each needs an independent melodic channel.");
        }

        var referenceBeats = songs[0].Beats;
        if (songs.Skip(1).Any(song => !HaveSameBeatTimeline(referenceBeats, song.Beats)))
        {
            throw new InvalidDataException("Combined MIDI export requires identical beat times for every arrangement so all tracks share one timeline.");
        }

        var trackBytes = new List<byte[]>(songs.Count + 1 + (_includeDrums ? 1 : 0))
        {
            CreateTrackBytes(CreateConductorEvents(songs[0]))
        };
        for (var index = 0; index < songs.Count; index++)
        {
            var song = songs[index];
            var track = song.Tracks[0];
            var noteCount = track.Notes.Count + track.Chords.Sum(chord => chord.Notes.Count);
            if (noteCount > MaximumNoteCount)
            {
                throw new InvalidDataException($"The '{track.Name}' arrangement exceeds the supported {MaximumNoteCount} MIDI note limit.");
            }

            var channel = ToMelodicChannel(index);
            trackBytes.Add(CreateTrackBytes(CreateEvents(song, track, channel, includeTempo: false,
                $"psarc2tab {track.Name}")));
        }

        if (_includeDrums)
        {
            trackBytes.Add(CreateTrackBytes(CreateDrumEvents(songs[0])));
        }

        WriteFile(destination, 1, trackBytes);
    }

    /// <summary>Builds a synthetic 4/4 drum pattern using the source beat grid.</summary>
    /// <param name="song">The song supplying beat timestamps and tempo changes.</param>
    /// <returns>Sorted General MIDI percussion events.</returns>
    /// <exception cref="InvalidDataException">The beat markers do not define complete four-beat measures.</exception>
    private static IReadOnlyList<MidiTrackEvent> CreateDrumEvents(Song song)
    {
        var beats = song.Beats;
        if (beats.Count < 4 || beats.Any(beat => beat.Beat is < 0 or > 3))
        {
            throw new InvalidDataException("Synthetic drums require beat markers that identify four-beat measures.");
        }

        for (var index = 1; index < beats.Count; index++)
        {
            var previous = beats[index - 1];
            var current = beats[index];
            var validNextBeat = current.Measure == previous.Measure && current.Beat == previous.Beat + 1;
            var validNextMeasure = current.Measure == previous.Measure + 1 && previous.Beat == 3 && current.Beat == 0;
            if (current.Time <= previous.Time || (!validNextBeat && !validNextMeasure))
            {
                throw new InvalidDataException("Synthetic drums require a continuous, ordered four-beat measure grid.");
            }
        }

        if (beats[0].Beat != 0 || beats[^1].Beat != 3)
        {
            throw new InvalidDataException("Synthetic drums require beat markers spanning complete four-beat measures.");
        }

        var tempoMap = TempoMap.Create(beats);
        var events = new List<MidiTrackEvent>
        {
            new(0, 0, CreateTextMetaEvent(0x03, "psarc2tab synthetic drums"))
        };
        foreach (var beat in beats)
        {
            var tick = tempoMap.ToMidiTick(beat.Time.Ticks);
            AddDrumHit(events, tick, 42);
            if (beat.Beat == 0)
            {
                AddDrumHit(events, tick, 36);
            }
            else if (beat.Beat == 2)
            {
                AddDrumHit(events, tick, 38);
            }
        }

        var lastTick = events.Max(item => item.Tick);
        events.Add(new MidiTrackEvent(lastTick, 10, [0xFF, 0x2F, 0x00]));
        return Array.AsReadOnly(events.OrderBy(item => item.Tick).ThenBy(item => item.Priority).ToArray());
    }

    /// <summary>Adds a short percussion note on General MIDI channel 10.</summary>
    /// <param name="events">The receiving events.</param>
    /// <param name="tick">The hit position.</param>
    /// <param name="pitch">The General MIDI percussion note.</param>
    private static void AddDrumHit(List<MidiTrackEvent> events, long tick, byte pitch)
    {
        events.Add(new MidiTrackEvent(tick, 5, [0x99, pitch, 100]));
        events.Add(new MidiTrackEvent(tick + TicksPerQuarterNote / 4, 4, [0x89, pitch, 0]));
    }

    /// <summary>Builds sorted metadata, note, and end-of-track events.</summary>
    /// <param name="song">The song containing the arrangement cent offset.</param>
    /// <param name="track">The arrangement to convert.</param>
    /// <returns>MIDI events ordered by absolute tick and event priority.</returns>
    private static IReadOnlyList<MidiTrackEvent> CreateEvents(Song song, Track track, int channel,
        bool includeTempo, string trackName)
    {
        var events = new List<MidiTrackEvent>();
        var tempoMap = TempoMap.Create(song.Beats);
        events.Add(new MidiTrackEvent(0, 0, CreateTextMetaEvent(0x03, trackName)));
        if (includeTempo)
        {
            foreach (var tempoChange in tempoMap.TempoChanges)
            {
                events.Add(new MidiTrackEvent(tempoChange.Tick, 1, CreateTempoMetaEvent(tempoChange.MicrosecondsPerQuarterNote)));
            }
        }

        events.Add(new MidiTrackEvent(0, 2, [(byte)(0xC0 | channel), GetProgramNumber(track.Name)]));
        if (song.CentOffsetCents != 0)
        {
            AddPitchBendEvents(events, song.CentOffsetCents, channel);
        }

        var pendingNotes = new List<PendingNoteEvent>();
        foreach (var note in track.Notes)
        {
            var pitch = GetMidiPitch(track.Tuning, note.StringNumber, note.FretNumber);
            pendingNotes.Add(new PendingNoteEvent(note.Position, note.Duration, pitch));
        }

        foreach (var chord in track.Chords)
        {
            foreach (var note in chord.Notes)
            {
                var pitch = GetMidiPitch(track.Tuning, note.StringNumber, note.FretNumber);
                pendingNotes.Add(new PendingNoteEvent(chord.Position, chord.Duration, pitch));
            }
        }

        var orderedNotes = pendingNotes.OrderBy(item => item.Position).ToArray();
        TimeSpan? nextPosition = null;
        var groupEnd = orderedNotes.Length - 1;
        while (groupEnd >= 0)
        {
            var position = orderedNotes[groupEnd].Position;
            var groupStart = groupEnd;
            while (groupStart > 0 && orderedNotes[groupStart - 1].Position == position)
            {
                groupStart--;
            }

            for (var index = groupStart; index <= groupEnd; index++)
            {
                var note = orderedNotes[index];
                var duration = note.Duration;
                if (duration == TimeSpan.Zero && nextPosition is { } next && next > position)
                {
                    duration = next - position;
                }

                AddNoteEvents(events, note.Position, duration, note.Pitch, tempoMap, channel);
            }

            nextPosition = position;
            groupEnd = groupStart - 1;
        }

        var lastTick = events.Max(item => item.Tick);
        events.Add(new MidiTrackEvent(lastTick, 10, [0xFF, 0x2F, 0x00]));
        return Array.AsReadOnly(events.OrderBy(item => item.Tick).ThenBy(item => item.Priority).ToArray());
    }

    /// <summary>Builds the shared tempo track used by a type-one MIDI file.</summary>
    /// <param name="song">The first arrangement, whose beat timeline has been checked against all others.</param>
    /// <returns>Sorted conductor-track events.</returns>
    private static IReadOnlyList<MidiTrackEvent> CreateConductorEvents(Song song)
    {
        var tempoMap = TempoMap.Create(song.Beats);
        var events = new List<MidiTrackEvent>
        {
            new(0, 0, CreateTextMetaEvent(0x03, "psarc2tab tempo map"))
        };
        events.AddRange(tempoMap.TempoChanges.Select(change => new MidiTrackEvent(change.Tick, 1,
            CreateTempoMetaEvent(change.MicrosecondsPerQuarterNote))));
        var lastTick = events.Max(item => item.Tick);
        events.Add(new MidiTrackEvent(lastTick, 10, [0xFF, 0x2F, 0x00]));
        return Array.AsReadOnly(events.OrderBy(item => item.Tick).ThenBy(item => item.Priority).ToArray());
    }

    /// <summary>Checks that two arrangements use the same beat timestamps.</summary>
    /// <param name="left">The reference timeline.</param>
    /// <param name="right">The timeline being compared.</param>
    /// <returns><see langword="true"/> when both timestamp sequences match exactly.</returns>
    private static bool HaveSameBeatTimeline(IReadOnlyList<BeatMarker> left, IReadOnlyList<BeatMarker> right) =>
        left.Count == right.Count && left.Zip(right).All(pair => pair.First.Time == pair.Second.Time);

    /// <summary>Assigns a distinct melodic channel while skipping General MIDI's percussion channel.</summary>
    /// <param name="arrangementIndex">The zero-based arrangement position.</param>
    /// <returns>A zero-based melodic channel number.</returns>
    private static int ToMelodicChannel(int arrangementIndex) => arrangementIndex < 9
        ? arrangementIndex
        : arrangementIndex + 1;

    /// <summary>Encodes global cents offset as a channel pitch bend and its sensitivity setting.</summary>
    /// <param name="events">The event list receiving the setup messages.</param>
    /// <param name="centOffset">The arrangement pitch offset in cents.</param>
    /// <exception cref="InvalidDataException">The offset exceeds the supported 127-semitone bend range.</exception>
    private static void AddPitchBendEvents(List<MidiTrackEvent> events, decimal centOffset, int channel)
    {
        if (centOffset is < -12_700 or > 12_700)
        {
            throw new InvalidDataException("The arrangement cent offset exceeds the supported MIDI pitch bend range.");
        }

        var absoluteOffset = decimal.Abs(centOffset);
        var bendRangeSemitones = Math.Max(2, (int)decimal.Ceiling(absoluteOffset / 100));
        var controlChangeStatus = (byte)(0xB0 | channel);
        events.Add(new MidiTrackEvent(0, 3, [controlChangeStatus, 0x65, 0x00]));
        events.Add(new MidiTrackEvent(0, 3, [controlChangeStatus, 0x64, 0x00]));
        events.Add(new MidiTrackEvent(0, 3, [controlChangeStatus, 0x06, (byte)bendRangeSemitones]));
        events.Add(new MidiTrackEvent(0, 3, [controlChangeStatus, 0x26, 0x00]));
        events.Add(new MidiTrackEvent(0, 3, [controlChangeStatus, 0x65, 0x7F]));
        events.Add(new MidiTrackEvent(0, 3, [controlChangeStatus, 0x64, 0x7F]));

        var bendDistance = centOffset >= 0 ? 8191m : 8192m;
        var bendValue = 8192 + (int)decimal.Round(centOffset / (bendRangeSemitones * 100m) * bendDistance,
            MidpointRounding.AwayFromZero);
        bendValue = Math.Clamp(bendValue, 0, 16_383);
        events.Add(new MidiTrackEvent(0, 4,
            [(byte)(0xE0 | channel), (byte)(bendValue & 0x7F), (byte)((bendValue >> 7) & 0x7F)]));
    }

    /// <summary>Adds note-on and note-off events for one pitch.</summary>
    /// <param name="events">The event list receiving note messages.</param>
    /// <param name="position">The note start time.</param>
    /// <param name="duration">The note duration.</param>
    /// <param name="pitch">The MIDI pitch number.</param>
    private static void AddNoteEvents(List<MidiTrackEvent> events, TimeSpan position, TimeSpan duration, byte pitch,
        TempoMap tempoMap, int channel)
    {
        var startTick = tempoMap.ToMidiTick(position.Ticks);
        var endTick = tempoMap.ToMidiTick((decimal)position.Ticks + duration.Ticks);
        if (endTick <= startTick)
        {
            endTick = checked(startTick + 1);
        }

        events.Add(new MidiTrackEvent(startTick, 6, [(byte)(0x90 | channel), pitch, 100]));
        events.Add(new MidiTrackEvent(endTick, 5, [(byte)(0x80 | channel), pitch, 0]));
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

    /// <summary>Creates a three-byte MIDI tempo meta event.</summary>
    /// <param name="microsecondsPerQuarterNote">The tempo in microseconds per quarter note.</param>
    /// <returns>The encoded tempo event without a delta time.</returns>
    private static byte[] CreateTempoMetaEvent(int microsecondsPerQuarterNote)
    {
        return [0xFF, 0x51, 0x03,
            (byte)((microsecondsPerQuarterNote >> 16) & 0xFF),
            (byte)((microsecondsPerQuarterNote >> 8) & 0xFF),
            (byte)(microsecondsPerQuarterNote & 0xFF)];
    }

    /// <summary>Maps source seconds onto MIDI ticks and records tempo changes at beat boundaries.</summary>
    private sealed class TempoMap
    {
        /// <summary>The source beat markers.</summary>
        private readonly IReadOnlyList<BeatMarker> _beats;
        /// <summary>The MIDI tick corresponding to each source beat.</summary>
        private readonly long[] _anchorTicks;
        /// <summary>The source-time span between each pair of beats.</summary>
        private readonly decimal[] _intervalTicks;
        /// <summary>The tempo events to write into the MIDI track.</summary>
        private readonly IReadOnlyList<TempoChange> _tempoChanges;
        /// <summary>Whether the map uses direct absolute-time conversion.</summary>
        private readonly bool _usesFallback;

        /// <summary>Creates a tempo map from ordered source beat markers.</summary>
        /// <param name="beats">The source beats.</param>
        private TempoMap(IReadOnlyList<BeatMarker> beats)
        {
            _beats = beats;
            _usesFallback = beats.Count < 2;
            if (_usesFallback)
            {
                _anchorTicks = [];
                _intervalTicks = [];
                _tempoChanges = [new TempoChange(0, FallbackMicrosecondsPerQuarterNote)];
                return;
            }

            _intervalTicks = new decimal[beats.Count - 1];
            var intervalTempos = new int[_intervalTicks.Length];
            for (var index = 0; index < _intervalTicks.Length; index++)
            {
                _intervalTicks[index] = beats[index + 1].Time.Ticks - beats[index].Time.Ticks;
                var tempo = decimal.Round(_intervalTicks[index] * 1_000_000m / TimeSpan.TicksPerSecond,
                    MidpointRounding.AwayFromZero);
                if (tempo is < 1 or > 0x00FF_FFFF)
                {
                    throw new InvalidDataException("A chart beat interval cannot be represented as a MIDI tempo.");
                }

                intervalTempos[index] = (int)tempo;
            }

            _anchorTicks = new long[beats.Count];
            _anchorTicks[0] = checked((long)decimal.Round(
                beats[0].Time.Ticks * (decimal)TicksPerQuarterNote / _intervalTicks[0],
                MidpointRounding.AwayFromZero));
            for (var index = 1; index < _anchorTicks.Length; index++)
            {
                _anchorTicks[index] = checked(_anchorTicks[index - 1] + TicksPerQuarterNote);
            }

            var changes = new List<TempoChange> { new(0, intervalTempos[0]) };
            for (var index = 1; index < intervalTempos.Length; index++)
            {
                if (intervalTempos[index] != intervalTempos[index - 1])
                {
                    changes.Add(new TempoChange(_anchorTicks[index], intervalTempos[index]));
                }
            }

            _tempoChanges = Array.AsReadOnly(changes.ToArray());
        }

        /// <summary>Gets the tempo meta events in chronological order.</summary>
        public IReadOnlyList<TempoChange> TempoChanges => _tempoChanges;

        /// <summary>Creates the fallback or source-beat tempo map.</summary>
        /// <param name="beats">The ordered beat markers.</param>
        /// <returns>The MIDI tempo map.</returns>
        public static TempoMap Create(IReadOnlyList<BeatMarker> beats) => new(beats);

        /// <summary>Converts a source timeline position to the nearest MIDI tick.</summary>
        /// <param name="timelineTicks">The position in <see cref="TimeSpan.Ticks"/> from the beginning of the song.</param>
        /// <returns>The nearest nonnegative MIDI tick.</returns>
        public long ToMidiTick(decimal timelineTicks)
        {
            if (_usesFallback)
            {
                var ticksPerSecond = TicksPerQuarterNote * 1_000_000m / FallbackMicrosecondsPerQuarterNote;
                return checked((long)decimal.Round(timelineTicks * ticksPerSecond / TimeSpan.TicksPerSecond,
                    MidpointRounding.AwayFromZero));
            }

            decimal mappedTick;
            if (timelineTicks <= _beats[0].Time.Ticks)
            {
                mappedTick = _anchorTicks[0] + (timelineTicks - _beats[0].Time.Ticks) *
                    TicksPerQuarterNote / _intervalTicks[0];
            }
            else if (timelineTicks >= _beats[^1].Time.Ticks)
            {
                mappedTick = _anchorTicks[^1] + (timelineTicks - _beats[^1].Time.Ticks) *
                    TicksPerQuarterNote / _intervalTicks[^1];
            }
            else
            {
                var lower = 0;
                var upper = _beats.Count - 1;
                while (upper - lower > 1)
                {
                    var middle = lower + (upper - lower) / 2;
                    if (timelineTicks <= _beats[middle].Time.Ticks)
                    {
                        upper = middle;
                    }
                    else
                    {
                        lower = middle;
                    }
                }

                var intervalIndex = lower;
                mappedTick = _anchorTicks[intervalIndex] + (timelineTicks - _beats[intervalIndex].Time.Ticks) *
                    TicksPerQuarterNote / _intervalTicks[intervalIndex];
            }

            return Math.Max(0, checked((long)decimal.Round(mappedTick, MidpointRounding.AwayFromZero)));
        }
    }

    /// <summary>Stores one MIDI tempo event at its absolute tick.</summary>
    /// <param name="Tick">The tempo event's tick position.</param>
    /// <param name="MicrosecondsPerQuarterNote">The quarter-note duration in microseconds.</param>
    private sealed record TempoChange(long Tick, int MicrosecondsPerQuarterNote);

    /// <summary>Serializes sorted MIDI events to a sized track chunk.</summary>
    /// <param name="events">The track events in tick order.</param>
    /// <returns>The encoded track body, including end-of-track.</returns>
    private static byte[] CreateTrackBytes(IReadOnlyList<MidiTrackEvent> events)
    {
        using var trackStream = new MemoryStream();
        WriteTrack(trackStream, events);
        return trackStream.ToArray();
    }

    /// <summary>Writes an SMF header followed by each already-encoded track chunk.</summary>
    /// <param name="destination">The destination stream.</param>
    /// <param name="format">The SMF format number.</param>
    /// <param name="tracks">The encoded track bodies.</param>
    private static void WriteFile(Stream destination, ushort format, IReadOnlyList<byte[]> tracks)
    {
        destination.Write("MThd"u8);
        WriteUInt32(destination, 6);
        WriteUInt16(destination, format);
        WriteUInt16(destination, checked((ushort)tracks.Count));
        WriteUInt16(destination, TicksPerQuarterNote);
        foreach (var track in tracks)
        {
            destination.Write("MTrk"u8);
            WriteUInt32(destination, checked((uint)track.Length));
            destination.Write(track);
        }
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

    /// <summary>Stores a source note until its effective MIDI duration is known.</summary>
    /// <param name="Position">The note onset.</param>
    /// <param name="Duration">The explicit source duration, if positive.</param>
    /// <param name="Pitch">The resulting MIDI pitch.</param>
    private sealed record PendingNoteEvent(TimeSpan Position, TimeSpan Duration, byte Pitch);
}
