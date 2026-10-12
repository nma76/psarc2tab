namespace Psarc2Tab.Domain;

/// <summary>Represents a lyric fragment positioned on the song timeline.</summary>
public sealed record TimedLyric
{
    /// <summary>Creates one validated timed lyric fragment.</summary>
    /// <param name="position">The start time relative to the beginning of the song.</param>
    /// <param name="text">The lyric text displayed at that time.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="position"/> is negative.</exception>
    /// <exception cref="ArgumentException"><paramref name="text"/> is null or whitespace.</exception>
    public TimedLyric(TimeSpan position, string text)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(position, TimeSpan.Zero);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        Position = position;
        Text = text;
    }

    /// <summary>Gets the lyric start time relative to the song beginning.</summary>
    public TimeSpan Position { get; }

    /// <summary>Gets the lyric text.</summary>
    public string Text { get; }
}
