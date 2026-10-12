namespace Psarc2Tab.Domain;

/// <summary>Contains timed lyric fragments extracted from one song.</summary>
public sealed class SongLyrics
{
    /// <summary>Creates a read-only collection of timed lyric fragments.</summary>
    /// <param name="lyrics">The fragments in source order.</param>
    /// <exception cref="ArgumentNullException"><paramref name="lyrics"/> is null.</exception>
    /// <exception cref="ArgumentException">The collection contains a null fragment.</exception>
    public SongLyrics(IEnumerable<TimedLyric> lyrics)
    {
        ArgumentNullException.ThrowIfNull(lyrics);
        var lyricArray = lyrics.ToArray();
        if (lyricArray.Any(lyric => lyric is null))
        {
            throw new ArgumentException("A lyrics document cannot contain a null fragment.", nameof(lyrics));
        }

        Lyrics = Array.AsReadOnly(lyricArray);
    }

    /// <summary>Gets the lyric fragments in source order.</summary>
    public IReadOnlyList<TimedLyric> Lyrics { get; }
}
