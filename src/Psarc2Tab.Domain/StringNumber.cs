namespace Psarc2Tab.Domain;

/// <summary>Represents a one-based string position, numbered in tuning order.</summary>
public readonly record struct StringNumber
{
    /// <summary>Creates a positive string number.</summary>
    /// <param name="value">The one-based string position.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is not positive.</exception>
    public StringNumber(int value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "String numbers start at one.");
        }

        Value = value;
    }

    /// <summary>Gets the one-based string position.</summary>
    public int Value { get; }
}
