using Psarc2Tab.Domain;

namespace Psarc2Tab.Application;

/// <summary>Coordinates lyric import and text-document writing for one archive.</summary>
public sealed class ExportLyricsUseCase
{
    /// <summary>Imports timed lyrics from the source archive.</summary>
    private readonly IArchiveLyricsImporter _lyricsImporter;

    /// <summary>Writes imported lyrics in the selected text format.</summary>
    private readonly ILyricsDocumentWriter _lyricsWriter;

    /// <summary>Creates a lyric export use case from its source and output adapters.</summary>
    /// <param name="lyricsImporter">The archive lyric importer.</param>
    /// <param name="lyricsWriter">The lyric document writer.</param>
    /// <exception cref="ArgumentNullException">An adapter is null.</exception>
    public ExportLyricsUseCase(IArchiveLyricsImporter lyricsImporter, ILyricsDocumentWriter lyricsWriter)
    {
        ArgumentNullException.ThrowIfNull(lyricsImporter);
        ArgumentNullException.ThrowIfNull(lyricsWriter);
        _lyricsImporter = lyricsImporter;
        _lyricsWriter = lyricsWriter;
    }

    /// <summary>Imports lyrics from an archive.</summary>
    /// <param name="archive">The readable, seekable archive stream.</param>
    /// <returns>The imported lyrics.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="archive"/> is null.</exception>
    public SongLyrics Import(Stream archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        return _lyricsImporter.Import(archive);
    }

    /// <summary>Writes an imported lyric document.</summary>
    /// <param name="lyrics">The imported lyrics.</param>
    /// <param name="destination">The destination writer.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public void Write(SongLyrics lyrics, TextWriter destination)
    {
        ArgumentNullException.ThrowIfNull(lyrics);
        ArgumentNullException.ThrowIfNull(destination);
        _lyricsWriter.Write(lyrics, destination);
    }
}
