using Psarc2Tab.Domain;
using Psarc2Tab.Tablature;

namespace Psarc2Tab.Tablature.Tests;

/// <summary>Verifies plain-text LRC output for timed lyric fragments.</summary>
public sealed class LrcLyricsWriterTests
{
    /// <summary>Groups lyric fragments until Rocksmith's explicit line marker.</summary>
    [Fact]
    public void Write_LineMarkers_ProducesTimestampedLrcLines()
    {
        var lyrics = new SongLyrics(
        [
            new TimedLyric(TimeSpan.FromMilliseconds(1234), "Hello"),
            new TimedLyric(TimeSpan.FromMilliseconds(1500), "world+"),
            new TimedLyric(TimeSpan.FromSeconds(62), "Again+")
        ]);
        using var output = new StringWriter();

        new LrcLyricsWriter().Write(lyrics, output);

        Assert.Equal("[00:01.23]Hello world\n[01:02.00]Again\n", output.ToString());
    }

    /// <summary>Rejects a document without non-empty lyric fragments.</summary>
    [Fact]
    public void Write_EmptyLyrics_ThrowsInvalidOperationException()
    {
        using var output = new StringWriter();

        Assert.Throws<InvalidOperationException>(() => new LrcLyricsWriter().Write(new SongLyrics([]), output));
    }

    /// <summary>Uses stable LF line endings even when the destination writer defaults to CRLF.</summary>
    [Fact]
    public void Write_UsesPlatformIndependentLineEndings()
    {
        using var output = new StringWriter { NewLine = "\r\n" };
        var lyrics = new SongLyrics([new TimedLyric(TimeSpan.Zero, "Hello+")]);

        new LrcLyricsWriter().Write(lyrics, output);

        Assert.Equal("[00:00.00]Hello\n", output.ToString());
    }
}
