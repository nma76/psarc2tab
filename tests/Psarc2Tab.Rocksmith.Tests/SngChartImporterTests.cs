using System.Text;
using Psarc2Tab.Formats.Rocksmith;

namespace Psarc2Tab.Rocksmith.Tests;

/// <summary>Verifies conversion of decoded SNG chart sections into domain events.</summary>
public sealed class SngChartImporterTests
{
    /// <summary>Imports the highest level with tuning offsets, timing, string position, and techniques.</summary>
    [Fact]
    public void Import_ChartWithLevels_UsesHighestDifficultyAndMapsNote()
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

    /// <summary>Rejects a truncated chart rather than reading outside the buffer.</summary>
    [Fact]
    public void Import_TruncatedChart_ThrowsInvalidDataException()
    {
        Assert.Throws<InvalidDataException>(() => new SngChartImporter().Import([0, 0, 0], CreateArrangement()));
    }

    /// <summary>Creates SNG arrangement metadata for an importer test.</summary>
    /// <param name="instrument">The arrangement label.</param>
    /// <param name="tuning">The six semitone offsets.</param>
    /// <param name="archivePath">The chart path used to select byte order.</param>
    /// <returns>An SNG arrangement descriptor.</returns>
    private static RocksmithArrangement CreateArrangement(string instrument = "Lead", int[]? tuning = null, string archivePath = "songs/bin/generic/test_lead.sng") =>
        new(1, instrument, archivePath, RocksmithArrangementFormat.Sng, "manifests/test.json",
            new RocksmithSngMetadata("Example Song", "Example Artist", tuning ?? [0, 0, 0, 0, 0, 0]));

    /// <summary>Builds a small SNG chart containing the requested levels and sections.</summary>
    /// <param name="levels">Difficulty and note pairs.</param>
    /// <param name="withChord">Whether to include one chord template.</param>
    /// <param name="bigEndian">Whether to write numeric fields in big-endian order.</param>
    /// <param name="sections">Optional section records.</param>
    /// <returns>The chart bytes.</returns>
    private static byte[] CreateChart((int difficulty, TestNote note)[] levels, bool withChord = false, bool bigEndian = false,
        (string name, int number, float start, float end)[]? sections = null)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(0);
        writer.Write(0);
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
        writer.Write(0);
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
        for (var index = 0; index < 3; index++) writer.Write(0);
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

    /// <summary>Stores the values written into one synthetic note.</summary>
    /// <param name="Mask">The note mask.</param>
    /// <param name="Time">The start time.</param>
    /// <param name="StringIndex">The zero-based string index.</param>
    /// <param name="Fret">The fret.</param>
    /// <param name="ChordId">The chord-template reference.</param>
    /// <param name="Sustain">The duration.</param>
    private sealed record TestNote(uint Mask, float Time, sbyte StringIndex, sbyte Fret, int ChordId, float Sustain);
}
