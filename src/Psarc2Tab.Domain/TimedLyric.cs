namespace Psarc2Tab.Domain;

/// <summary>Represents a lyric fragment positioned on the song timeline.</summary>
public sealed record TimedLyric
{
    /// <summary>Creates one validated timed lyric fragment.</summary>
    /// <param name="position">The start time relative to the beginning of the song.</param>
    /// <param name="text">The lyric text displayed at that time.</param>
    /// <param name="duration">The optional syllable duration.</param>
    /// <exception cref="ArgumentOutOfRangeException">A timing value is negative.</exception>
    /// <exception cref="ArgumentException"><paramref name="text"/> is null or whitespace.</exception>
    public TimedLyric(TimeSpan position, string text, TimeSpan duration = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(position, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        Position = position;
        Text = text;
        Duration = duration;
    }

    /// <summary>Gets the lyric start time relative to the song beginning.</summary>
    public TimeSpan Position { get; }

    /// <summary>Gets the lyric text.</summary>
    public string Text { get; }

    /// <summary>Gets the syllable duration, or zero when the source does not provide one.</summary>
    public TimeSpan Duration { get; }
}
