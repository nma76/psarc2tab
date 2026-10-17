using System.Buffers.Binary;
using Psarc2Tab.Domain;
using Psarc2Tab.Midi;

namespace Psarc2Tab.Midi.Tests;

/// <summary>Verifies Standard MIDI File structure and exported musical events.</summary>
public sealed class StandardMidiFileWriterTests
{
    /// <summary>Writes a type-zero file with the selected timing division and tempo.</summary>
    [Fact]
    public void Write_SimpleNote_WritesTypeZeroHeaderTempoAndTimedNote()
    {
        var track = CreateTrack("Lead", [new NoteEvent(TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(0.25), new StringNumber(1), new FretNumber(3))]);
        var song = new Song("Song", "Artist", [track]);

        var bytes = Write(song, track);
        var events = ReadEvents(bytes);

        Assert.Equal("MThd", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(8, 2)));
        Assert.Equal((ushort)1, BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(10, 2)));
        Assert.Equal((ushort)960, BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(12, 2)));
        Assert.Contains(events, item => item.Data.SequenceEqual(new byte[] { 0xFF, 0x51, 0x07, 0xA1, 0x20 }));
        Assert.Contains(events, item => item.Tick == 960 && item.Data.SequenceEqual(new byte[] { 0x90, 43, 100 }));
        Assert.Contains(events, item => item.Tick == 1440 && item.Data.SequenceEqual(new byte[] { 0x80, 43, 0 }));
    }

    /// <summary>Writes a tempo change at a beat while preserving source-time spacing.</summary>
    [Fact]
    public void Write_BeatMarkers_WritesTempoMapAndMapsNoteTimingToBeats()
    {
        var track = CreateTrack("Lead", [new NoteEvent(TimeSpan.FromSeconds(0.25), TimeSpan.FromSeconds(0.5),
            new StringNumber(1), new FretNumber(3))]);
        var song = new Song("Song", "Artist", [track], beats:
        [
            new BeatMarker(TimeSpan.Zero, 0, 0),
            new BeatMarker(TimeSpan.FromSeconds(0.5), 0, 1),
            new BeatMarker(TimeSpan.FromSeconds(1.5), 0, 2)
        ]);

        var events = ReadEvents(Write(song, track));

        Assert.Contains(events, item => item.Tick == 0 &&
            item.Data.SequenceEqual(new byte[] { 0xFF, 0x51, 0x07, 0xA1, 0x20 }));
        Assert.Contains(events, item => item.Tick == 960 &&
            item.Data.SequenceEqual(new byte[] { 0xFF, 0x51, 0x0F, 0x42, 0x40 }));
        Assert.Contains(events, item => item.Tick == 480 && item.Data.SequenceEqual(new byte[] { 0x90, 43, 100 }));
        Assert.Contains(events, item => item.Tick == 1200 && item.Data.SequenceEqual(new byte[] { 0x80, 43, 0 }));
    }

    /// <summary>Expands chord tones as simultaneous MIDI note events.</summary>
    [Fact]
    public void Write_Chord_WritesEveryToneAtTheSameTick()
    {
        var chord = new ChordEvent(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(0.5),
        [
            new FrettedNote(new StringNumber(1), new FretNumber(2)),
            new FrettedNote(new StringNumber(2), new FretNumber(3))
        ]);
        var track = CreateTrack("Lead", [], [chord]);

        var events = ReadEvents(Write(new Song("Song", "", [track]), track));
        var noteOns = events.Where(item => item.Tick == 1920 && item.Data[0] == 0x90).ToArray();

        Assert.Equal([42, 48], noteOns.Select(item => (int)item.Data[1]).Order().ToArray());
    }

    /// <summary>Uses the General MIDI electric bass program for bass arrangements.</summary>
    [Fact]
    public void Write_BassArrangement_WritesFingeredBassProgram()
    {
        var track = CreateTrack("Bass", []);

        var events = ReadEvents(Write(new Song("Song", "", [track]), track));

        Assert.Contains(events, item => item.Data.SequenceEqual(new byte[] { 0xC0, 33 }));
    }

    /// <summary>Encodes a nonzero arrangement cent offset with pitch-bend setup events.</summary>
    [Fact]
    public void Write_CentOffset_WritesPitchBendRangeAndValue()
    {
        var track = CreateTrack("Lead", []);

        var events = ReadEvents(Write(new Song("Song", "", [track], centOffsetCents: -20m), track));

        Assert.Contains(events, item => item.Data.SequenceEqual(new byte[] { 0xB0, 0x06, 2 }));
        Assert.Contains(events, item => item.Data[0] == 0xE0 && item.Data[2] < 64);
    }

    /// <summary>Rejects unsupported arrangement names and pitches outside the MIDI range.</summary>
    [Fact]
    public void Write_UnsupportedInstrumentOrPitch_ThrowsInvalidDataException()
    {
        var unsupported = CreateTrack("Vocals", []);
        Assert.Throws<InvalidDataException>(() => Write(new Song("Song", "", [unsupported]), unsupported));

        var tooHigh = new Track("Lead", new GuitarTuning([120, 40, 45, 50, 55, 59]),
            [new NoteEvent(TimeSpan.Zero, TimeSpan.FromSeconds(1), new StringNumber(1), new FretNumber(8))], []);
        Assert.Throws<InvalidDataException>(() => Write(new Song("Song", "", [tooHigh]), tooHigh));
    }

    /// <summary>Preserves a zero-length source note as a one-tick MIDI note.</summary>
    [Fact]
    public void Write_ZeroDurationNote_UsesOneTickMinimum()
    {
        var track = CreateTrack("Lead", [new NoteEvent(TimeSpan.Zero, TimeSpan.Zero, new StringNumber(1), new FretNumber(0))]);

        var events = ReadEvents(Write(new Song("Song", "", [track]), track));

        Assert.Contains(events, item => item.Tick == 0 && item.Data[0] == 0x90);
        Assert.Contains(events, item => item.Tick == 1 && item.Data[0] == 0x80);
    }

    /// <summary>Creates a standard guitar-tuned arrangement for focused writer tests.</summary>
    /// <param name="name">The arrangement name.</param>
    /// <param name="notes">Individual notes to include.</param>
    /// <param name="chords">Optional chord events to include.</param>
    /// <returns>The requested track.</returns>
    private static Track CreateTrack(string name, IEnumerable<NoteEvent> notes, IEnumerable<ChordEvent>? chords = null) =>
        new(name, new GuitarTuning([40, 45, 50, 55, 59, 64]), notes, chords ?? []);

    /// <summary>Serializes one arrangement to an in-memory MIDI file.</summary>
    /// <param name="song">The song metadata.</param>
    /// <param name="track">The selected arrangement.</param>
    /// <returns>The encoded file bytes.</returns>
    private static byte[] Write(Song song, Track track)
    {
        using var output = new MemoryStream();
        new StandardMidiFileWriter().Write(song, track, output);
        return output.ToArray();
    }

    /// <summary>Reads MIDI track events used by the assertions in this test class.</summary>
    /// <param name="file">The encoded type-zero MIDI file.</param>
    /// <returns>Absolute ticks and event payloads.</returns>
    private static IReadOnlyList<ParsedEvent> ReadEvents(byte[] file)
    {
        var trackLength = checked((int)BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(18, 4)));
        var position = 22;
        var end = position + trackLength;
        long tick = 0;
        var events = new List<ParsedEvent>();
        while (position < end)
        {
            tick += ReadVariableLengthQuantity(file, ref position);
            var status = file[position++];
            if (status == 0xFF)
            {
                var type = file[position++];
                var size = ReadVariableLengthQuantity(file, ref position);
                var data = new byte[size + 2];
                data[0] = status;
                data[1] = type;
                file.AsSpan(position, size).CopyTo(data.AsSpan(2));
                position += size;
                events.Add(new ParsedEvent(tick, data));
            }
            else
            {
                var size = (status & 0xF0) == 0xC0 || (status & 0xF0) == 0xD0 ? 1 : 2;
                var data = new byte[size + 1];
                data[0] = status;
                file.AsSpan(position, size).CopyTo(data.AsSpan(1));
                position += size;
                events.Add(new ParsedEvent(tick, data));
            }
        }

        return events;
    }

    /// <summary>Reads one MIDI variable-length quantity.</summary>
    /// <param name="bytes">The complete MIDI file.</param>
    /// <param name="position">The cursor advanced while reading.</param>
    /// <returns>The decoded nonnegative integer.</returns>
    private static int ReadVariableLengthQuantity(byte[] bytes, ref int position)
    {
        var value = 0;
        byte next;
        do
        {
            next = bytes[position++];
            value = (value << 7) | (next & 0x7F);
        } while ((next & 0x80) != 0);

        return value;
    }

    /// <summary>Stores one parsed MIDI event at its absolute tick.</summary>
    /// <param name="Tick">The absolute time of the event.</param>
    /// <param name="Data">The event bytes after its delta time.</param>
    private sealed record ParsedEvent(long Tick, byte[] Data);
}
