using System.Text;
using Psarc2Tab.Domain;
using Psarc2Tab.Tablature;

namespace Psarc2Tab.Tablature.Tests;

/// <summary>Verifies heatmap counting and its stable ASCII rendering.</summary>
public sealed class FretboardHeatmapTextWriterTests
{
    /// <summary>Counts each note and each chord tone at its string/fret coordinate.</summary>
    [Fact]
    public void Calculate_NotesAndChordTones_CountsPositionUses()
    {
        var track = CreateTrack(
        [
            CreateNote(1, 3),
            CreateNote(1, 3),
            CreateNote(2, 0)
        ],
        [
            new ChordEvent(TimeSpan.FromSeconds(1), TimeSpan.Zero,
            [
                new FrettedNote(new StringNumber(1), new FretNumber(3)),
                new FrettedNote(new StringNumber(2), new FretNumber(2))
            ])
        ]);

        var heatmap = FretboardHeatmapCalculator.Calculate(track);

        Assert.Equal(3, heatmap.GetUseCount(new StringNumber(1), new FretNumber(3)));
        Assert.Equal(1, heatmap.GetUseCount(new StringNumber(2), new FretNumber(0)));
        Assert.Equal(1, heatmap.GetUseCount(new StringNumber(2), new FretNumber(2)));
        Assert.Equal(3, heatmap.MaximumUseCount);
        Assert.Equal(24, heatmap.MaximumFret);
    }

    /// <summary>Keeps an empty arrangement on the standard neck range with zero intensity.</summary>
    [Fact]
    public void Calculate_EmptyTrack_UsesStandardNeckAndHasNoCounts()
    {
        var heatmap = FretboardHeatmapCalculator.Calculate(CreateTrack([], []));

        Assert.Equal(24, heatmap.MaximumFret);
        Assert.Equal(0, heatmap.MaximumUseCount);
    }

    /// <summary>Expands the standard fret range when the chart uses a higher fret.</summary>
    [Fact]
    public void Calculate_HighFret_ExpandsRange()
    {
        var heatmap = FretboardHeatmapCalculator.Calculate(CreateTrack([CreateNote(1, 27)], []));

        Assert.Equal(27, heatmap.MaximumFret);
        Assert.Equal(1, heatmap.GetUseCount(new StringNumber(1), new FretNumber(27)));
    }

    /// <summary>Accepts the highest supported fret used by rare extended-fret instruments.</summary>
    [Fact]
    public void Calculate_MaximumSupportedFret_ExpandsRange()
    {
        var track = CreateTrack([CreateNote(1, FretboardHeatmapCalculator.MaximumSupportedFret)], []);

        var heatmap = FretboardHeatmapCalculator.Calculate(track);

        Assert.Equal(FretboardHeatmapCalculator.MaximumSupportedFret, heatmap.MaximumFret);
        Assert.Equal(1, heatmap.GetUseCount(new StringNumber(1),
            new FretNumber(FretboardHeatmapCalculator.MaximumSupportedFret)));
    }

    /// <summary>Rejects implausibly high fret values before allocating or rendering a large grid.</summary>
    [Fact]
    public void Calculate_FretAboveDisplayLimit_ThrowsInvalidDataException()
    {
        var track = CreateTrack([CreateNote(1, FretboardHeatmapCalculator.MaximumSupportedFret + 1)], []);

        var exception = Assert.Throws<InvalidDataException>(() => FretboardHeatmapCalculator.Calculate(track));

        Assert.Contains("exceeds the ASCII heatmap limit", exception.Message);
    }

    /// <summary>Uses fixed count bands and displays strings from highest pitch to lowest.</summary>
    [Fact]
    public void Write_UsesStableIntensityBandsAndTuningOrder()
    {
        var notes = Enumerable.Range(1, 5).SelectMany((fret, index) =>
            Enumerable.Range(0, new[] { 1, 2, 4, 8, 16 }[index]).Select(_ => CreateNote(1, fret))).ToArray();
        var track = CreateTrack(notes, []);
        var song = new Song("Test", "Artist", [track]);
        using var output = new MemoryStream();

        new FretboardHeatmapTextWriter().Write(song, track, output);
        var text = Encoding.UTF8.GetString(output.ToArray());
        var lines = text.Split('\n');

        Assert.Contains("Fretboard heatmap (beta)", text);
        Assert.Contains("Uses per position: blank=0, .=1, :=2-3, +=4-7, #=8-15, @=16+", text);
        Assert.Contains("Maximum uses at one position: 16", text);
        Assert.True(Array.FindIndex(lines, line => line.StartsWith("2 A2", StringComparison.Ordinal)) <
            Array.FindIndex(lines, line => line.StartsWith("1 E2", StringComparison.Ordinal)));
        Assert.Contains("  .", text);
        Assert.Contains("  :", text);
        Assert.Contains("  +", text);
        Assert.Contains("  #", text);
        Assert.Contains("  @", text);
    }

    /// <summary>Produces identical output for the same song and track.</summary>
    [Fact]
    public void Write_SameInput_ProducesDeterministicOutput()
    {
        var track = CreateTrack([CreateNote(1, 4), CreateNote(2, 8)], []);
        var song = new Song("Test", "Artist", [track]);
        using var first = new MemoryStream();
        using var second = new MemoryStream();
        var writer = new FretboardHeatmapTextWriter();

        writer.Write(song, track, first);
        writer.Write(song, track, second);

        Assert.Equal(first.ToArray(), second.ToArray());
    }

    /// <summary>Creates a note event at one coordinate.</summary>
    /// <param name="stringNumber">The one-based string number.</param>
    /// <param name="fretNumber">The fret number.</param>
    /// <returns>A zero-duration note event.</returns>
    private static NoteEvent CreateNote(int stringNumber, int fretNumber) =>
        new(TimeSpan.Zero, TimeSpan.Zero, new StringNumber(stringNumber), new FretNumber(fretNumber));

    /// <summary>Creates a two-string test track with standard E/A tuning.</summary>
    /// <param name="notes">The standalone notes.</param>
    /// <param name="chords">The chord events.</param>
    /// <returns>A Lead track with low E and A strings.</returns>
    private static Track CreateTrack(IEnumerable<NoteEvent> notes, IEnumerable<ChordEvent> chords) =>
        new("Lead", new GuitarTuning([40, 45]), notes, chords);
}
