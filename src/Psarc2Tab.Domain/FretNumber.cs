namespace Psarc2Tab.Domain;

/// <summary>Represents a nonnegative fret position, with zero denoting an open string.</summary>
public readonly record struct FretNumber
{
    /// <summary>Creates a valid fret position.</summary>
    /// <param name="value">The fret number.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative.</exception>
    public FretNumber(int value)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Fret numbers cannot be negative.");
        }

        Value = value;
    }

    /// <summary>Gets the fret number.</summary>
    public int Value { get; }
}
