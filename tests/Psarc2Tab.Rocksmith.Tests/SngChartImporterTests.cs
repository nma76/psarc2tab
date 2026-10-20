using System.Buffers.Binary;
using System.Text;
using Psarc2Tab.Application;
using Psarc2Tab.Domain;
using Psarc2Tab.Formats.Rocksmith;

namespace Psarc2Tab.Rocksmith.Tests;

/// <summary>Verifies conversion of decoded SNG chart sections into domain events.</summary>
public sealed class SngChartImporterTests
{
    /// <summary>Imports notes at the full arrangement difficulty for each phrase iteration.</summary>
    [Fact]
    public void Import_ChartWithLevels_UsesPhraseIterationMasterDifficultyAndMapsNote()
    {
        var arrangement = CreateArrangement();
        var chart = CreateChart([
            (difficulty: 0, note: new TestNote(0x800000, 1.0f, 0, 2, -1, 0.25f)),
            (difficulty: 12, note: new TestNote(0x800000 | 0x200 | 0x800, 3.5f, 4, 7, -1, 1.25f))]);

        var song = new SngChartImporter().Import(chart, arrangement);
        var track = Assert.Single(song.Tracks);
        var note = Assert.Single(track.Notes);

        Assert.Equal("Example Song", song.Title);
        Assert.Equal("Example Artist", song.Artist);
        Assert.Equal(new[] { 40, 45, 50, 55, 59, 64 }, track.Tuning.OpenStringMidiPitches);
        Assert.Equal(5, note.StringNumber.Value);
        Assert.Equal(7, note.FretNumber.Value);
        Assert.Equal(TimeSpan.FromSeconds(3.5), note.Position);
        Assert.Equal(TimeSpan.FromSeconds(1.25), note.Duration);
        Assert.True(note.Techniques.HasFlag(Psarc2Tab.Domain.NoteTechnique.HammerOn));
        Assert.True(note.Techniques.HasFlag(Psarc2Tab.Domain.NoteTechnique.Slide));
    }

    /// <summary>Calculates the same arrangement statistics from an imported SNG song model.</summary>
    [Fact]
    public void Import_ChartStatistics_UsesImportedSngEvents()
    {
        var arrangement = CreateArrangement();
        var chart = CreateChart(
            [(difficulty: 12, note: new TestNote(0x800000 | 0x200 | 0x800 | 0x1000, 3.5f, 4, 7, -1, 1.25f))],
            sections: [("verse", 1, 1.0f, 5.0f)]);
        var song = new SngChartImporter().Import(chart, arrangement);

        var statistics = ArrangementStatisticsCalculator.Calculate(song);

        Assert.Equal(1, statistics.NoteCount);
        Assert.Equal(0, statistics.ChordCount);
        Assert.Equal(1, statistics.SectionCount);
        Assert.Equal(1, statistics.BendCount);
        Assert.Equal(1, statistics.SlideCount);
        Assert.Equal(1, statistics.HammerOnCount);
        Assert.Equal(new[] { 5 }, statistics.UsedStrings);
        Assert.Equal(7, statistics.MinimumFret);
        Assert.Equal(7, statistics.MaximumFret);
        Assert.Equal(TimeSpan.FromSeconds(4.75), statistics.Length);
    }

    /// <summary>Copies the arrangement cent offset from its manifest metadata into the song model.</summary>
    [Fact]
    public void Import_CentOffset_MapsToSong()
    {
        var arrangement = CreateArrangement(centOffsetCents: -33.5m);
        var chart = CreateChart([(difficulty: 12, note: new TestNote(0x800000, 1.0f, 0, 2, -1, 0.25f))]);

        var song = new SngChartImporter().Import(chart, arrangement);

        Assert.Equal(-33.5m, song.CentOffsetCents);
    }

    /// <summary>Selects the full difficulty independently for each phrase iteration.</summary>
    [Fact]
    public void Import_PhraseIterations_UsesTheirOwnMasterDifficulties()
    {
        var chart = CreateChart([
            (difficulty: 10, note: new TestNote(0x800000, 25.0f, 1, 2, -1, 0, 1)),
            (difficulty: 12, note: new TestNote(0x800000, 15.0f, 0, 4, -1, 0, 0)),
            (difficulty: 14, note: new TestNote(0x800000, 50.0f, 0, 9, -1, 0, 0))],
            masterDifficulties: [12, 10]);

        var song = new SngChartImporter().Import(chart, CreateArrangement());

        Assert.Collection(Assert.Single(song.Tracks).Notes.OrderBy(note => note.Position),
            note => { Assert.Equal(TimeSpan.FromSeconds(15), note.Position); Assert.Equal(4, note.FretNumber.Value); },
            note => { Assert.Equal(TimeSpan.FromSeconds(25), note.Position); Assert.Equal(2, note.FretNumber.Value); });
    }

    /// <summary>Maps a bass tuning and chord fret template to domain objects.</summary>
    [Fact]
    public void Import_BassChord_MapsBassPitchesAndTemplate()
    {
        var arrangement = CreateArrangement("Bass", [0, 0, 0, 0, 0, 0]);
        var chart = CreateChart([(12, new TestNote(0x2, 2.0f, -1, -1, 0, 0.75f))], withChord: true);

        var song = new SngChartImporter().Import(chart, arrangement);
        var track = Assert.Single(song.Tracks);
        var chord = Assert.Single(track.Chords);

        Assert.Equal(new[] { 28, 33, 38, 43, 47, 52 }, track.Tuning.OpenStringMidiPitches);
        Assert.Equal("C", chord.Label);
        Assert.Equal(3, chord.Notes.Count);
        Assert.Equal(3, chord.Notes[1].StringNumber.Value);
        Assert.Equal(3, chord.Notes[1].FretNumber.Value);
        Assert.Equal(TimeSpan.FromSeconds(0.75), chord.Duration);
    }

    /// <summary>Imports section headings from the fixed-size PC section records.</summary>
    [Fact]
    public void Import_Sections_MapsNameNumberAndStartTime()
    {
        var chart = CreateChart([(0, new TestNote(0, 0, 0, 0, -1, 0))], sections: [("intro", 1, 1.25f, 4.5f), ("verse", 2, 4.5f, 9.0f)]);

        var song = new SngChartImporter().Import(chart, CreateArrangement());

        Assert.Collection(song.Sections,
            section => { Assert.Equal("intro", section.Name); Assert.Equal(1, section.Number); Assert.Equal(TimeSpan.FromSeconds(1.25), section.StartTime); },
            section => { Assert.Equal("verse", section.Name); Assert.Equal(2, section.Number); Assert.Equal(TimeSpan.FromSeconds(4.5), section.StartTime); });
    }

    /// <summary>Reads section timing fields using the Mac chart byte order.</summary>
    [Fact]
    public void Import_MacSections_UsesBigEndianFields()
    {
        var chart = CreateChart([(0, new TestNote(0, 0, 0, 0, -1, 0))], bigEndian: true,
            sections: [("chorus", 3, 12.5f, 18.0f)]);

        var song = new SngChartImporter().Import(chart, CreateArrangement("Lead", archivePath: "songs/bin/macos/test_lead.sng"));

        var section = Assert.Single(song.Sections);
        Assert.Equal("chorus", section.Name);
        Assert.Equal(3, section.Number);
        Assert.Equal(TimeSpan.FromSeconds(12.5), section.StartTime);
    }

    /// <summary>Imports beat times and source coordinates in either chart byte order.</summary>
    /// <param name="bigEndian">Whether the synthetic chart uses Mac byte order.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Import_Beats_PreservesBeatPositions(bool bigEndian)
    {
        var chart = CreateChart([(12, new TestNote(0x800000, 0.25f, 0, 2, -1, 0.1f))], bigEndian: bigEndian,
            beats: [(0.0f, 0, 0), (0.5f, 0, 1), (1.5f, 0, 2)]);
        var archivePath = bigEndian ? "songs/bin/macos/test_lead.sng" : "songs/bin/generic/test_lead.sng";

        var song = new SngChartImporter().Import(chart, CreateArrangement("Lead", archivePath: archivePath));

        Assert.Collection(song.Beats,
            beat => Assert.Equal(new BeatMarker(TimeSpan.Zero, 0, 0), beat),
            beat => Assert.Equal(new BeatMarker(TimeSpan.FromSeconds(0.5), 0, 1), beat),
            beat => Assert.Equal(new BeatMarker(TimeSpan.FromSeconds(1.5), 0, 2), beat));
    }

    /// <summary>Rejects a truncated chart rather than reading outside the buffer.</summary>
    [Fact]
    public void Import_TruncatedChart_ThrowsInvalidDataException()
    {
        Assert.Throws<InvalidDataException>(() => new SngChartImporter().Import([0, 0, 0], CreateArrangement()));
    }

    /// <summary>Rejects negative array counts before attempting to read chart records.</summary>
    [Fact]
    public void Import_NegativeArrayCount_ThrowsInvalidDataException()
    {
        var chart = CreateChart([(12, new TestNote(0x800000, 1, 0, 2, -1, 0.25f))]);
        BinaryPrimitives.WriteInt32LittleEndian(chart, -1);

        Assert.Throws<InvalidDataException>(() => new SngChartImporter().Import(chart, CreateArrangement()));
    }

    /// <summary>Rejects finite SNG time values that cannot fit the Domain timeline.</summary>
    [Fact]
    public void Import_ExtremeFiniteNoteTime_ThrowsInvalidDataException()
    {
        var chart = CreateChart([(12, new TestNote(0x800000, float.MaxValue, 0, 2, -1, 0.25f))]);
        var sustainChart = CreateChart([(12, new TestNote(0x800000, 1, 0, 2, -1, float.MaxValue))]);

        Assert.Throws<InvalidDataException>(() => new SngChartImporter().Import(chart, CreateArrangement()));
        Assert.Throws<InvalidDataException>(() => new SngChartImporter().Import(sustainChart, CreateArrangement()));
    }

    /// <summary>Rejects extreme section and lyric times instead of leaking range exceptions.</summary>
    [Fact]
    public void Import_ExtremeFiniteSectionAndLyricTimes_ThrowsInvalidDataException()
    {
        var sectionChart = CreateChart([(12, new TestNote(0x800000, 1, 0, 2, -1, 0.25f))],
            sections: [("intro", 1, float.MaxValue, float.MaxValue)]);
        var lyricChart = CreateLyricsChart([(float.MaxValue, "Hello")]);
        var importer = new SngChartImporter();

        Assert.Throws<InvalidDataException>(() => importer.Import(sectionChart, CreateArrangement()));
        Assert.Throws<InvalidDataException>(() => importer.ImportLyrics(lyricChart, "songs/bin/generic/test_vocals.sng"));
    }

    /// <summary>Imports lyric text and timing from fixed-size PC vocal records.</summary>
    [Fact]
    public void ImportLyrics_PcRecords_ReturnsTimedFragments()
    {
        var chart = CreateLyricsChart([(1.25f, "Hello"), (2.5f, "world+")]);

        var lyrics = new SngChartImporter().ImportLyrics(chart, "songs/bin/generic/sample_vocals.sng");

        Assert.Collection(lyrics.Lyrics,
            lyric => { Assert.Equal(TimeSpan.FromSeconds(1.25), lyric.Position); Assert.Equal("Hello", lyric.Text); },
            lyric => { Assert.Equal(TimeSpan.FromSeconds(2.5), lyric.Position); Assert.Equal("world+", lyric.Text); });
    }

    /// <summary>Reads vocal timing fields using the Mac chart byte order.</summary>
    [Fact]
    public void ImportLyrics_MacRecords_UsesBigEndianFields()
    {
        var chart = CreateLyricsChart([(12.5f, "bonjour")], bigEndian: true);

        var lyrics = new SngChartImporter().ImportLyrics(chart, "songs/bin/macos/sample_vocals.sng");

        var lyric = Assert.Single(lyrics.Lyrics);
        Assert.Equal(TimeSpan.FromSeconds(12.5), lyric.Position);
        Assert.Equal("bonjour", lyric.Text);
    }

    /// <summary>Creates SNG arrangement metadata for an importer test.</summary>
    /// <param name="instrument">The arrangement label.</param>
    /// <param name="tuning">The six semitone offsets.</param>
    /// <param name="archivePath">The chart path used to select byte order.</param>
    /// <param name="centOffsetCents">The arrangement pitch offset.</param>
    /// <returns>An SNG arrangement descriptor.</returns>
    private static RocksmithArrangement CreateArrangement(string instrument = "Lead", int[]? tuning = null,
        string archivePath = "songs/bin/generic/test_lead.sng", decimal centOffsetCents = 0) =>
        new(1, instrument, archivePath, RocksmithArrangementFormat.Sng, "manifests/test.json",
            new RocksmithSngMetadata("Example Song", "Example Artist", tuning ?? [0, 0, 0, 0, 0, 0], centOffsetCents));

    /// <summary>Builds a small SNG chart containing the requested levels and sections.</summary>
    /// <param name="levels">Difficulty and note pairs.</param>
    /// <param name="withChord">Whether to include one chord template.</param>
    /// <param name="bigEndian">Whether to write numeric fields in big-endian order.</param>
    /// <param name="sections">Optional section records.</param>
    /// <param name="masterDifficulties">Optional full difficulties, one per phrase iteration.</param>
    /// <param name="beats">Optional beat time and measure coordinates.</param>
    /// <returns>The chart bytes.</returns>
    private static byte[] CreateChart((int difficulty, TestNote note)[] levels, bool withChord = false, bool bigEndian = false,
        (string name, int number, float start, float end)[]? sections = null, int[]? masterDifficulties = null,
        (float time, int measure, int beat)[]? beats = null)
    {
        masterDifficulties ??= [levels.Max(level => level.difficulty)];
        beats ??= [];
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        WriteInt32(writer, beats.Length, bigEndian);
        foreach (var beat in beats)
        {
            WriteSingle(writer, beat.time, bigEndian);
            WriteUInt16(writer, checked((ushort)beat.measure), bigEndian);
            WriteUInt16(writer, checked((ushort)beat.beat), bigEndian);
            WriteInt32(writer, 0, bigEndian);
            WriteInt32(writer, 0, bigEndian);
        }

        WriteInt32(writer, masterDifficulties.Length, bigEndian);
        for (var index = 0; index < masterDifficulties.Length; index++) writer.Write(new byte[44]);
        if (withChord)
        {
            writer.Write(1);
            writer.Write(0u);
            writer.Write((sbyte)0);
            writer.Write((sbyte)-1);
            writer.Write((sbyte)3);
            writer.Write((sbyte)-1);
            writer.Write((sbyte)5);
            writer.Write((sbyte)-1);
            for (var index = 0; index < 6; index++) writer.Write((sbyte)-1);
            writer.Write(new byte[24]);
            writer.Write(Encoding.UTF8.GetBytes("C\0".PadRight(32, '\0')));
        }
        else
        {
            writer.Write(0);
        }

        writer.Write(0);
        writer.Write(0);
        WriteInt32(writer, masterDifficulties.Length, bigEndian);
        for (var index = 0; index < masterDifficulties.Length; index++)
        {
            WriteInt32(writer, index, bigEndian);
            WriteSingle(writer, 0, bigEndian);
            WriteSingle(writer, 100, bigEndian);
            WriteInt32(writer, masterDifficulties[index], bigEndian);
            WriteInt32(writer, masterDifficulties[index], bigEndian);
            WriteInt32(writer, masterDifficulties[index], bigEndian);
        }

        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        WriteInt32(writer, sections?.Length ?? 0, bigEndian);
        foreach (var section in sections ?? [])
        {
            writer.Write(Encoding.UTF8.GetBytes(section.name.PadRight(32, '\0')));
            WriteInt32(writer, section.number, bigEndian);
            WriteSingle(writer, section.start, bigEndian);
            WriteSingle(writer, section.end, bigEndian);
            writer.Write(new byte[8]);
            writer.Write(new byte[36]);
        }

        WriteInt32(writer, levels.Length, bigEndian);
        foreach (var (difficulty, note) in levels)
        {
            WriteInt32(writer, difficulty, bigEndian);
            for (var index = 0; index < 4; index++) writer.Write(0);
            WriteInt32(writer, 1, bigEndian);
            WriteNote(writer, note, bigEndian);
            for (var index = 0; index < 3; index++) writer.Write(0);
        }

        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>Builds the SNG header sections required to reach a vocal record array.</summary>
    /// <param name="lyrics">The time and text pairs to encode.</param>
    /// <param name="bigEndian">Whether numeric fields use Mac byte order.</param>
    /// <returns>The decoded chart bytes.</returns>
    private static byte[] CreateLyricsChart((float time, string text)[] lyrics, bool bigEndian = false)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        WriteInt32(writer, lyrics.Length, bigEndian);
        foreach (var lyric in lyrics)
        {
            WriteSingle(writer, lyric.time, bigEndian);
            WriteInt32(writer, 60, bigEndian);
            WriteSingle(writer, 0.25f, bigEndian);
            var text = Encoding.UTF8.GetBytes(lyric.text);
            if (text.Length > 48)
            {
                throw new ArgumentException("Test lyric text exceeds the SNG field width.", nameof(lyrics));
            }

            writer.Write(text);
            writer.Write(new byte[48 - text.Length]);
        }

        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>Writes the fields consumed from one SNG note record.</summary>
    /// <param name="writer">The chart writer.</param>
    /// <param name="note">The test note values.</param>
    /// <param name="bigEndian">Whether to write numeric fields in big-endian order.</param>
    private static void WriteNote(BinaryWriter writer, TestNote note, bool bigEndian)
    {
        WriteInt32(writer, unchecked((int)note.Mask), bigEndian);
        writer.Write(0u);
        writer.Write(0u);
        WriteSingle(writer, note.Time, bigEndian);
        writer.Write(note.StringIndex);
        writer.Write(note.Fret);
        writer.Write((sbyte)0);
        writer.Write((sbyte)0);
        WriteInt32(writer, note.ChordId, bigEndian);
        WriteInt32(writer, 0, bigEndian);
        WriteInt32(writer, note.PhraseIterationId, bigEndian);
        WriteInt32(writer, note.PhraseIterationId, bigEndian);
        for (var index = 0; index < 5; index++) writer.Write((short)0);
        for (var index = 0; index < 7; index++) writer.Write((sbyte)0);
        writer.Write((short)0);
        WriteSingle(writer, note.Sustain, bigEndian);
        writer.Write(0.0f);
        writer.Write(0);
    }

    /// <summary>Writes a signed integer in the selected chart byte order.</summary>
    /// <param name="writer">The chart writer.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="bigEndian">Whether to use big-endian byte order.</param>
    private static void WriteInt32(BinaryWriter writer, int value, bool bigEndian)
    {
        var bytes = BitConverter.GetBytes(value);
        if (bigEndian) Array.Reverse(bytes);
        writer.Write(bytes);
    }

    /// <summary>Writes a floating-point value in the selected chart byte order.</summary>
    /// <param name="writer">The chart writer.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="bigEndian">Whether to use big-endian byte order.</param>
    private static void WriteSingle(BinaryWriter writer, float value, bool bigEndian) =>
        WriteInt32(writer, BitConverter.SingleToInt32Bits(value), bigEndian);

    /// <summary>Writes an unsigned 16-bit integer in the selected chart byte order.</summary>
    /// <param name="writer">The chart writer.</param>
    /// <param name="value">The value to encode.</param>
    /// <param name="bigEndian">Whether to use big-endian byte order.</param>
    private static void WriteUInt16(BinaryWriter writer, ushort value, bool bigEndian)
    {
        var bytes = BitConverter.GetBytes(value);
        if (bigEndian) Array.Reverse(bytes);
        writer.Write(bytes);
    }

    /// <summary>Stores the values written into one synthetic note.</summary>
    /// <param name="Mask">The note mask.</param>
    /// <param name="Time">The start time.</param>
    /// <param name="StringIndex">The zero-based string index.</param>
    /// <param name="Fret">The fret.</param>
    /// <param name="ChordId">The chord-template reference.</param>
    /// <param name="Sustain">The duration.</param>
    /// <param name="PhraseIterationId">The phrase iteration containing the test note.</param>
    private sealed record TestNote(uint Mask, float Time, sbyte StringIndex, sbyte Fret, int ChordId, float Sustain,
        int PhraseIterationId = 0);
}
