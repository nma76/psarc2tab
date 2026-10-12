using Psarc2Tab.Domain;

namespace Psarc2Tab.Application;

/// <summary>Writes timed song lyrics in a text-based lyrics format.</summary>
public interface ILyricsDocumentWriter
{
    /// <summary>Writes the supplied lyrics.</summary>
    /// <param name="lyrics">The imported lyrics.</param>
    /// <param name="destination">The destination text writer.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    void Write(SongLyrics lyrics, TextWriter destination);
}
