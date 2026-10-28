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

    /// <summary>Writes a separate percussion track with kick, snare, and hi-hat hits on a 4/4 beat grid.</summary>
    [Fact]
    public void Write_WithDrums_AddsGeneralMidiPercussionTrack()
    {
        var track = CreateTrack("Lead", []);
        var song = new Song("Song", "Artist", [track], beats:
        [
            new BeatMarker(TimeSpan.Zero, 0, 0),
            new BeatMarker(TimeSpan.FromSeconds(0.5), 0, 1),
            new BeatMarker(TimeSpan.FromSeconds(1.5), 0, 2),
            new BeatMarker(TimeSpan.FromSeconds(2), 0, 3)
        ]);
        using var output = new MemoryStream();

        new StandardMidiFileWriter(includeDrums: true).Write(song, track, output);

        var bytes = output.ToArray();
        Assert.Equal((ushort)2, BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(10, 2)));
        var drumEvents = ReadEventsFromTrack(ReadTracks(bytes)[1]);
        Assert.Contains(drumEvents, item => item.Tick == 0 && item.Data.SequenceEqual(new byte[] { 0x99, 36, 100 }));
        Assert.Contains(drumEvents, item => item.Tick == 0 && item.Data.SequenceEqual(new byte[] { 0x99, 42, 100 }));
        Assert.Contains(drumEvents, item => item.Tick == 960 && item.Data.SequenceEqual(new byte[] { 0x99, 42, 100 }));
        Assert.Contains(drumEvents, item => item.Tick == 1920 && item.Data.SequenceEqual(new byte[] { 0x99, 38, 100 }));
        Assert.Contains(ReadEvents(bytes), item => item.Tick == 960 &&
            item.Data.SequenceEqual(new byte[] { 0xFF, 0x51, 0x0F, 0x42, 0x40 }));
        Assert.All(drumEvents.Where(item => item.Data.Length >= 3 && (item.Data[0] & 0x0F) == 9),
            item => Assert.Equal(9, item.Data[0] & 0x0F));
    }

    /// <summary>Rejects synthetic drums when beat markers do not define a complete 4/4 measure.</summary>
    [Fact]
    public void Write_WithDrumsAndIncompleteBeatGrid_ThrowsInvalidDataException()
    {
        var track = CreateTrack("Lead", []);
        var song = new Song("Song", "Artist", [track], beats:
        [
            new BeatMarker(TimeSpan.Zero, 0, 0),
            new BeatMarker(TimeSpan.FromSeconds(0.5), 0, 1)
        ]);

        using var output = new MemoryStream();
        Assert.Throws<InvalidDataException>(() => new StandardMidiFileWriter(includeDrums: true)
            .Write(song, track, output));
    }

    /// <summary>Adds a shared synthetic percussion track to a combined type-one MIDI file.</summary>
    [Fact]
    public void WriteMultiple_WithDrums_AddsPercussionTrackAfterArrangementTracks()
    {
        var song = CreateSong("Lead", 0,
        [
            new BeatMarker(TimeSpan.Zero, 0, 0),
            new BeatMarker(TimeSpan.FromSeconds(0.5), 0, 1),
            new BeatMarker(TimeSpan.FromSeconds(1), 0, 2),
            new BeatMarker(TimeSpan.FromSeconds(1.5), 0, 3)
        ]);
        using var output = new MemoryStream();

        new StandardMidiFileWriter(includeDrums: true).WriteMultiple([song], output);

        var bytes = output.ToArray();
        Assert.Equal((ushort)1, BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(8, 2)));
        Assert.Equal((ushort)3, BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(10, 2)));
        Assert.Contains(ReadEventsFromTrack(ReadTracks(bytes)[2]), item =>
            item.Tick == 1920 && item.Data.SequenceEqual(new byte[] { 0x99, 38, 100 }));
    }

    /// <summary>Expands chord tones as simultaneous MIDI events with the explicit source duration.</summary>
    [Fact]
    public void Write_Chord_WritesEveryToneAtTheSameTick()
    {
        var chord = new ChordEvent(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(0.5),
        [
            new FrettedNote(new StringNumber(1), new FretNumber(2)),
            new FrettedNote(new StringNumber(2), new FretNumber(3))
        ]);
        var track = CreateTrack("Lead", [], [chord]);

        var song = new Song("Song", "", [track], beats:
        [
            new BeatMarker(TimeSpan.Zero, 0, 0),
            new BeatMarker(TimeSpan.FromSeconds(0.5), 0, 1),
            new BeatMarker(TimeSpan.FromSeconds(1.5), 0, 2)
        ]);
        var events = ReadEvents(Write(song, track));
        var noteOns = events.Where(item => item.Tick == 1440 && item.Data[0] == 0x90).ToArray();

        Assert.Equal([42, 48], noteOns.Select(item => (int)item.Data[1]).Order().ToArray());
        Assert.Equal([1920, 1920], events.Where(item => item.Data[0] == 0x80).Select(item => item.Tick).Order().ToArray());
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

    /// <summary>Extends a zero-duration note to the next onset on any string.</summary>
    [Fact]
    public void Write_ZeroDurationNote_EndsAtNextOnsetOnDifferentString()
    {
        var track = CreateTrack("Lead",
        [
            new NoteEvent(TimeSpan.Zero, TimeSpan.Zero, new StringNumber(1), new FretNumber(0)),
            new NoteEvent(TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(0.25), new StringNumber(2), new FretNumber(2))
        ]);

        var events = ReadEvents(Write(new Song("Song", "", [track]), track));

        Assert.Contains(events, item => item.Tick == 0 && item.Data.SequenceEqual(new byte[] { 0x90, 40, 100 }));
        Assert.Contains(events, item => item.Tick == 960 && item.Data.SequenceEqual(new byte[] { 0x80, 40, 0 }));
        Assert.Contains(events, item => item.Tick == 960 && item.Data.SequenceEqual(new byte[] { 0x90, 47, 100 }));
    }

    /// <summary>Ends every zero-duration chord tone at the next arrangement onset.</summary>
    [Fact]
    public void Write_ZeroDurationChord_EndsAtNextArrangementOnset()
    {
        var chord = new ChordEvent(TimeSpan.Zero, TimeSpan.Zero,
        [
            new FrettedNote(new StringNumber(1), new FretNumber(0)),
            new FrettedNote(new StringNumber(2), new FretNumber(0))
        ]);
        var track = CreateTrack("Lead",
            [new NoteEvent(TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(0.25), new StringNumber(1), new FretNumber(2))], [chord]);

        var events = ReadEvents(Write(new Song("Song", "", [track]), track));

        Assert.Contains(events, item => item.Tick == 960 && item.Data.SequenceEqual(new byte[] { 0x80, 40, 0 }));
        Assert.Contains(events, item => item.Tick == 960 && item.Data.SequenceEqual(new byte[] { 0x80, 45, 0 }));
    }

    /// <summary>Does not let a zero-duration note ring through an intervening different-string attack.</summary>
    [Fact]
    public void Write_ZeroDurationNote_DoesNotRingPastInterveningDifferentStringAttack()
    {
        var track = CreateTrack("Lead",
        [
            new NoteEvent(TimeSpan.Zero, TimeSpan.Zero, new StringNumber(1), new FretNumber(0)),
            new NoteEvent(TimeSpan.FromSeconds(0.25), TimeSpan.FromSeconds(0.25), new StringNumber(2), new FretNumber(2)),
            new NoteEvent(TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(0.25), new StringNumber(1), new FretNumber(3))
        ]);

        var events = ReadEvents(Write(new Song("Song", "", [track]), track));

        Assert.Contains(events, item => item.Tick == 480 && item.Data.SequenceEqual(new byte[] { 0x80, 40, 0 }));
        Assert.DoesNotContain(events, item => item.Tick == 960 && item.Data.SequenceEqual(new byte[] { 0x80, 40, 0 }));
    }

    /// <summary>Writes a type-one file with a tempo track and isolated arrangement channels in input order.</summary>
    [Fact]
    public void WriteMultiple_Arrangements_WritesTempoTrackAndOrderedTracksOnSeparateChannels()
    {
        var beats = new[]
        {
            new BeatMarker(TimeSpan.Zero, 0, 0),
            new BeatMarker(TimeSpan.FromSeconds(0.5), 0, 1),
            new BeatMarker(TimeSpan.FromSeconds(1.5), 0, 2)
        };
        var songs = new[]
        {
            CreateSong("Rhythm", 0, beats),
            CreateSong("Lead", 2, beats),
            CreateSong("Bass", 4, beats)
        };

        var bytes = WriteMultiple(songs);
        var tracks = ReadTracks(bytes);

        Assert.Equal((ushort)1, BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(8, 2)));
        Assert.Equal((ushort)4, BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(10, 2)));
        Assert.Equal("psarc2tab tempo map", GetTrackName(tracks[0]));
        Assert.Equal(["psarc2tab Rhythm", "psarc2tab Lead", "psarc2tab Bass"],
            tracks.Skip(1).Select(GetTrackName));
        var tempoEvents = tracks.SelectMany(ReadTrackEvents)
            .Where(item => item.Data.Length >= 2 && item.Data[0] == 0xFF && item.Data[1] == 0x51)
            .ToArray();
        Assert.Equal(2, tempoEvents.Length);
        Assert.DoesNotContain(tracks.Skip(1).SelectMany(ReadTrackEvents),
            item => item.Data.Length >= 2 && item.Data[0] == 0xFF && item.Data[1] == 0x51);
        Assert.Contains(ReadTrackEvents(tracks[1]), item => item.Data.SequenceEqual(new byte[] { 0x90, 40, 100 }));
        Assert.Contains(ReadTrackEvents(tracks[2]), item => item.Data.SequenceEqual(new byte[] { 0x91, 42, 100 }));
        Assert.Contains(ReadTrackEvents(tracks[3]), item => item.Data.SequenceEqual(new byte[] { 0x92, 44, 100 }));
        Assert.Contains(ReadTrackEvents(tracks[3]), item => item.Data.SequenceEqual(new byte[] { 0xC2, 33 }));
    }

    /// <summary>Rejects combined exports whose beat markers would give tracks different timelines.</summary>
    [Fact]
    public void WriteMultiple_DifferentBeatTimelines_ThrowsInvalidDataException()
    {
        var first = CreateSong("Lead", 0,
        [
            new BeatMarker(TimeSpan.Zero, 0, 0),
            new BeatMarker(TimeSpan.FromSeconds(0.5), 0, 1)
        ]);
        var second = CreateSong("Bass", 2,
        [
            new BeatMarker(TimeSpan.Zero, 0, 0),
            new BeatMarker(TimeSpan.FromSeconds(0.6), 0, 1)
        ]);

        Assert.Throws<InvalidDataException>(() => WriteMultiple([first, second]));
    }

    /// <summary>Allows different measure coordinates when arrangements share beat timestamps.</summary>
    [Fact]
    public void WriteMultiple_SameBeatTimesWithDifferentCoordinates_WritesCombinedFile()
    {
        var first = CreateSong("Lead", 0,
        [
            new BeatMarker(TimeSpan.Zero, 1, 0),
            new BeatMarker(TimeSpan.FromSeconds(0.5), 1, 1)
        ]);
        var second = CreateSong("Bass", 2,
        [
            new BeatMarker(TimeSpan.Zero, 0, 0),
            new BeatMarker(TimeSpan.FromSeconds(0.5), 0, 2)
        ]);

        Assert.Equal((ushort)3, BinaryPrimitives.ReadUInt16BigEndian(WriteMultiple([first, second]).AsSpan(10, 2)));
    }

    /// <summary>Rejects empty and over-capacity combined exports.</summary>
    [Fact]
    public void WriteMultiple_InvalidArrangementCount_ThrowsInvalidDataException()
    {
        Assert.Throws<ArgumentException>(() => WriteMultiple([]));
        var song = CreateSong("Lead", 0, []);

        Assert.Throws<InvalidDataException>(() => WriteMultiple(Enumerable.Repeat(song, 16).ToArray()));
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
    private static byte[] Write(Song song, Track track, bool includeDrums = false)
    {
        using var output = new MemoryStream();
        new StandardMidiFileWriter(includeDrums).Write(song, track, output);
        return output.ToArray();
    }

    /// <summary>Serializes multiple arrangements into an in-memory MIDI file.</summary>
    /// <param name="songs">Arrangements in desired track order.</param>
    /// <returns>The encoded type-one MIDI file.</returns>
    private static byte[] WriteMultiple(IReadOnlyList<Song> songs)
    {
        using var output = new MemoryStream();
        new StandardMidiFileWriter().WriteMultiple(songs, output);
        return output.ToArray();
    }

    /// <summary>Creates one song with a single note and the supplied beat timeline.</summary>
    /// <param name="name">The arrangement name.</param>
    /// <param name="fret">The note fret.</param>
    /// <param name="beats">The shared beat markers.</param>
    /// <returns>A single-track song fixture.</returns>
    private static Song CreateSong(string name, int fret, IEnumerable<BeatMarker> beats)
    {
        var track = CreateTrack(name,
            [new NoteEvent(TimeSpan.Zero, TimeSpan.FromSeconds(0.25), new StringNumber(1), new FretNumber(fret))]);
        return new Song("Song", "Artist", [track], beats: beats);
    }

    /// <summary>Reads the format-one track chunks from a Standard MIDI File.</summary>
    /// <param name="file">The encoded MIDI file.</param>
    /// <returns>Track bodies in file order.</returns>
    private static IReadOnlyList<byte[]> ReadTracks(byte[] file)
    {
        var trackCount = BinaryPrimitives.ReadUInt16BigEndian(file.AsSpan(10, 2));
        var position = 14;
        var tracks = new List<byte[]>(trackCount);
        for (var index = 0; index < trackCount; index++)
        {
            Assert.Equal("MTrk", System.Text.Encoding.ASCII.GetString(file, position, 4));
            var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(position + 4, 4)));
            position += 8;
            tracks.Add(file.AsSpan(position, length).ToArray());
            position += length;
        }

        Assert.Equal(file.Length, position);
        return tracks;
    }

    /// <summary>Reads the initial track-name text meta event.</summary>
    /// <param name="track">The encoded MIDI track body.</param>
    /// <returns>The UTF-8 track name.</returns>
    private static string GetTrackName(byte[] track)
    {
        Assert.Equal(0, track[0]);
        Assert.Equal(0xFF, track[1]);
        Assert.Equal(0x03, track[2]);
        var nameLength = track[3] & 0x7F;
        return System.Text.Encoding.UTF8.GetString(track, 4, nameLength);
    }

    /// <summary>Reads all event payloads and absolute ticks from a MIDI track body.</summary>
    /// <param name="track">The encoded MIDI track body.</param>
    /// <returns>Parsed absolute ticks and event payloads.</returns>
    private static IReadOnlyList<ParsedEvent> ReadTrackEvents(byte[] track)
    {
        var position = 0;
        long tick = 0;
        var events = new List<ParsedEvent>();
        while (position < track.Length)
        {
            tick += ReadVariableLengthQuantity(track, ref position);
            var status = track[position++];
            if (status == 0xFF)
            {
                var type = track[position++];
                var size = ReadVariableLengthQuantity(track, ref position);
                var data = new byte[size + 2];
                data[0] = status;
                data[1] = type;
                track.AsSpan(position, size).CopyTo(data.AsSpan(2));
                position += size;
                events.Add(new ParsedEvent(tick, data));
            }
            else
            {
                var size = (status & 0xF0) == 0xC0 || (status & 0xF0) == 0xD0 ? 1 : 2;
                var data = new byte[size + 1];
                data[0] = status;
                track.AsSpan(position, size).CopyTo(data.AsSpan(1));
                position += size;
                events.Add(new ParsedEvent(tick, data));
            }
        }

        return events;
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

    /// <summary>Reads delta-time events from one MIDI track body.</summary>
    /// <param name="track">The encoded track body.</param>
    /// <returns>Events with absolute tick positions.</returns>
    private static IReadOnlyList<ParsedEvent> ReadEventsFromTrack(byte[] track)
    {
        using var file = new MemoryStream();
        file.Write("MThd"u8);
        file.Write([0, 0, 0, 6, 0, 0, 0, 1, 3, 192]);
        file.Write("MTrk"u8);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)track.Length);
        file.Write(length);
        file.Write(track);
        return ReadEvents(file.ToArray());
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
