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

    /// <summary>Rejects a truncated chart rather than reading outside the buffer.</summary>
    [Fact]
    public void Import_TruncatedChart_ThrowsInvalidDataException()
    {
        Assert.Throws<InvalidDataException>(() => new SngChartImporter().Import([0, 0, 0], CreateArrangement()));
    }

    /// <summary>Creates SNG arrangement metadata for an importer test.</summary>
    /// <param name="instrument">The arrangement label.</param>
    /// <param name="tuning">The six semitone offsets.</param>
    /// <returns>An SNG arrangement descriptor.</returns>
    private static RocksmithArrangement CreateArrangement(string instrument = "Lead", int[]? tuning = null) =>
        new(1, instrument, "songs/bin/generic/test_lead.sng", RocksmithArrangementFormat.Sng, "manifests/test.json",
            new RocksmithSngMetadata("Example Song", "Example Artist", tuning ?? [0, 0, 0, 0, 0, 0]));

    /// <summary>Builds a small little-endian SNG chart containing the requested levels.</summary>
    /// <param name="levels">Difficulty and note pairs.</param>
    /// <param name="withChord">Whether to include one chord template.</param>
    /// <returns>The chart bytes.</returns>
    private static byte[] CreateChart((int difficulty, TestNote note)[] levels, bool withChord = false)
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
        writer.Write(0);
        writer.Write(levels.Length);
        foreach (var (difficulty, note) in levels)
        {
            writer.Write(difficulty);
            for (var index = 0; index < 4; index++) writer.Write(0);
            writer.Write(1);
            WriteNote(writer, note);
            for (var index = 0; index < 3; index++) writer.Write(0);
        }

        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>Writes the fields consumed from one SNG note record.</summary>
    /// <param name="writer">The chart writer.</param>
    /// <param name="note">The test note values.</param>
    private static void WriteNote(BinaryWriter writer, TestNote note)
    {
        writer.Write(note.Mask);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(note.Time);
        writer.Write(note.StringIndex);
        writer.Write(note.Fret);
        writer.Write((sbyte)0);
        writer.Write((sbyte)0);
        writer.Write(note.ChordId);
        for (var index = 0; index < 3; index++) writer.Write(0);
        for (var index = 0; index < 5; index++) writer.Write((short)0);
        for (var index = 0; index < 7; index++) writer.Write((sbyte)0);
        writer.Write((short)0);
        writer.Write(note.Sustain);
        writer.Write(0.0f);
        writer.Write(0);
    }

    /// <summary>Stores the values written into one synthetic note.</summary>
    /// <param name="Mask">The note mask.</param>
    /// <param name="Time">The start time.</param>
    /// <param name="StringIndex">The zero-based string index.</param>
    /// <param name="Fret">The fret.</param>
    /// <param name="ChordId">The chord-template reference.</param>
    /// <param name="Sustain">The duration.</param>
    private sealed record TestNote(uint Mask, float Time, sbyte StringIndex, sbyte Fret, int ChordId, float Sustain);
}
