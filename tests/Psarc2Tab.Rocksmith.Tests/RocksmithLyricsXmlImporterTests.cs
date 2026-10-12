using System.Text;
using Psarc2Tab.Formats.Rocksmith;

namespace Psarc2Tab.Rocksmith.Tests;

/// <summary>Verifies timed lyric import from Rocksmith vocals XML.</summary>
public sealed class RocksmithLyricsXmlImporterTests
{
    /// <summary>Reads lyric entries in document order.</summary>
    [Fact]
    public void Import_ValidVocalsXml_ReturnsTimedLyrics()
    {
        using var source = new MemoryStream(Encoding.UTF8.GetBytes("""
            <vocals count="2">
              <vocal time="1.25" note="60" length="0.2" lyric="Hello" />
              <vocal time="2.5" note="62" length="0.3" lyric="world+" />
            </vocals>
            """));

        var lyrics = new RocksmithLyricsXmlImporter().Import(source);

        Assert.Collection(lyrics.Lyrics,
            lyric => { Assert.Equal(TimeSpan.FromSeconds(1.25), lyric.Position); Assert.Equal("Hello", lyric.Text); },
            lyric => { Assert.Equal(TimeSpan.FromSeconds(2.5), lyric.Position); Assert.Equal("world+", lyric.Text); });
    }

    /// <summary>Rejects XML documents with an unexpected root.</summary>
    [Fact]
    public void Import_UnexpectedRoot_ThrowsInvalidDataException()
    {
        using var source = new MemoryStream("<song />"u8.ToArray());

        Assert.Throws<InvalidDataException>(() => new RocksmithLyricsXmlImporter().Import(source));
    }

    /// <summary>Rejects vocal records without valid timing or text.</summary>
    [Fact]
    public void Import_InvalidVocalEntry_ThrowsInvalidDataException()
    {
        using var source = new MemoryStream("<vocals><vocal time=\"bad\" length=\"0\" lyric=\"Hi\" /></vocals>"u8.ToArray());

        Assert.Throws<InvalidDataException>(() => new RocksmithLyricsXmlImporter().Import(source));
    }
}
