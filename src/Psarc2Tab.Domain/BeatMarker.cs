namespace Psarc2Tab.Domain;

/// <summary>Represents a beat position retained from a song chart.</summary>
public sealed record BeatMarker
{
    /// <summary>Creates a beat marker with its source time and measure position.</summary>
    /// <param name="time">The beat time relative to the song timeline.</param>
    /// <param name="measure">The source measure value.</param>
    /// <param name="beat">The source beat value within its measure.</param>
    /// <exception cref="ArgumentOutOfRangeException">A time or index is negative.</exception>
    public BeatMarker(TimeSpan time, int measure, int beat)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(time, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegative(measure);
        ArgumentOutOfRangeException.ThrowIfNegative(beat);
        Time = time;
        Measure = measure;
        Beat = beat;
    }

    /// <summary>Gets the beat time relative to the song timeline.</summary>
    public TimeSpan Time { get; }

    /// <summary>Gets the source measure value.</summary>
    public int Measure { get; }

    /// <summary>Gets the source beat value within its measure.</summary>
    public int Beat { get; }
}
