using Psarc2Tab.Domain;

namespace Psarc2Tab.Tablature;

/// <summary>Counts note events and chord tones at their fretboard positions.</summary>
public static class FretboardHeatmapCalculator
{
    /// <summary>The highest fret supported by the fixed-width ASCII heatmap.</summary>
    public const int MaximumSupportedFret = 48;

    /// <summary>The minimum number of fret columns, used to keep heatmaps comparable.</summary>
    public const int StandardNeckFretCount = 24;

    /// <summary>Calculates counts for every used position and the standard neck range.</summary>
    /// <param name="track">The imported arrangement track.</param>
    /// <returns>A heatmap with one count for each note event and each chord tone.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="track"/> is null.</exception>
    /// <exception cref="InvalidDataException">An event uses a fret above the supported display limit.</exception>
    public static FretboardHeatmap Calculate(Track track)
    {
        ArgumentNullException.ThrowIfNull(track);
        var maximumUsedFret = track.Notes.Select(note => note.FretNumber.Value)
            .Concat(track.Chords.SelectMany(chord => chord.Notes).Select(note => note.FretNumber.Value))
            .DefaultIfEmpty(0)
            .Max();
        if (maximumUsedFret > MaximumSupportedFret)
        {
            throw new InvalidDataException(
                $"Fret {maximumUsedFret} exceeds the ASCII heatmap limit of {MaximumSupportedFret}.");
        }

        var maximumFret = Math.Max(StandardNeckFretCount, maximumUsedFret);
        var uses = new int[track.Tuning.StringCount, maximumFret + 1];
        foreach (var note in track.Notes)
        {
            AddUse(uses, note.StringNumber, note.FretNumber);
        }

        foreach (var chord in track.Chords)
        {
            foreach (var note in chord.Notes)
            {
                AddUse(uses, note.StringNumber, note.FretNumber);
            }
        }

        return new FretboardHeatmap(uses);
    }

    /// <summary>Adds one event to its string and fret position.</summary>
    /// <param name="uses">The mutable position-use matrix.</param>
    /// <param name="stringNumber">The one-based string number.</param>
    /// <param name="fretNumber">The fret number.</param>
    private static void AddUse(int[,] uses, StringNumber stringNumber, FretNumber fretNumber) =>
        uses[stringNumber.Value - 1, fretNumber.Value] = checked(uses[stringNumber.Value - 1, fretNumber.Value] + 1);
}
