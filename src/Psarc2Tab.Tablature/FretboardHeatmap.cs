using Psarc2Tab.Domain;

namespace Psarc2Tab.Tablature;

/// <summary>Stores note and chord-tone usage for each string and fret position.</summary>
public sealed class FretboardHeatmap
{
    /// <summary>The number of uses at each string and fret coordinate.</summary>
    private readonly int[,] _uses;

    /// <summary>Creates a heatmap from a calculated position-use matrix.</summary>
    /// <param name="uses">The use counts indexed by zero-based string index and fret.</param>
    /// <exception cref="ArgumentNullException"><paramref name="uses"/> is null.</exception>
    internal FretboardHeatmap(int[,] uses)
    {
        ArgumentNullException.ThrowIfNull(uses);
        _uses = (int[,])uses.Clone();
        StringCount = uses.GetLength(0);
        MaximumFret = uses.GetLength(1) - 1;
        MaximumUseCount = uses.Cast<int>().DefaultIfEmpty().Max();
    }

    /// <summary>Gets the number of strings represented by the heatmap.</summary>
    public int StringCount { get; }

    /// <summary>Gets the highest fret included in the heatmap.</summary>
    public int MaximumFret { get; }

    /// <summary>Gets the greatest count at any one string and fret position.</summary>
    public int MaximumUseCount { get; }

    /// <summary>Gets how many times a string and fret position occurs in the arrangement.</summary>
    /// <param name="stringNumber">The one-based string number in tuning order.</param>
    /// <param name="fretNumber">The fret to inspect, including zero for an open string.</param>
    /// <returns>The number of note events and chord tones at that position.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The string or fret is outside this heatmap.</exception>
    public int GetUseCount(StringNumber stringNumber, FretNumber fretNumber)
    {
        if (stringNumber.Value < 1 || stringNumber.Value > StringCount)
        {
            throw new ArgumentOutOfRangeException(nameof(stringNumber));
        }

        if (fretNumber.Value > MaximumFret)
        {
            throw new ArgumentOutOfRangeException(nameof(fretNumber));
        }

        return _uses[stringNumber.Value - 1, fretNumber.Value];
    }
}
