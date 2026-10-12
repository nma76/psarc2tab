using Psarc2Tab.Application;
using Psarc2Tab.Domain;

namespace Psarc2Tab.Formats.Rocksmith;

/// <summary>Extracts and imports timed lyrics from a supported PSARC archive.</summary>
public sealed class RocksmithArchiveLyricsImporter : IArchiveLyricsImporter
{
    /// <summary>Discovers and extracts the archive's vocal chart.</summary>
    private readonly RocksmithArrangementCatalog _catalog;

    /// <summary>Reads XML vocal chart entries.</summary>
    private readonly RocksmithLyricsXmlImporter _xmlImporter;

    /// <summary>Reads decoded SNG vocal chart entries.</summary>
    private readonly SngChartImporter _sngImporter;

    /// <summary>Creates an archive lyric importer from its format-specific readers.</summary>
    /// <param name="catalog">The PSARC arrangement catalog.</param>
    /// <param name="xmlImporter">An optional XML lyric importer.</param>
    /// <param name="sngImporter">An optional SNG chart importer.</param>
    /// <exception cref="ArgumentNullException"><paramref name="catalog"/> is null.</exception>
    public RocksmithArchiveLyricsImporter(RocksmithArrangementCatalog catalog,
        RocksmithLyricsXmlImporter? xmlImporter = null, SngChartImporter? sngImporter = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
        _xmlImporter = xmlImporter ?? new RocksmithLyricsXmlImporter();
        _sngImporter = sngImporter ?? new SngChartImporter();
    }

    /// <inheritdoc />
    public SongLyrics Import(Stream archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        using var payload = new MemoryStream();
        var source = _catalog.ExtractLyrics(archive, payload);
        payload.Position = 0;

        SongLyrics lyrics;
        if (source.DataFormat == RocksmithArrangementFormat.Sng)
        {
            var decoded = new SngContainerReader().Decode(payload, SngContainerReader.GetPlatform(source.ArchivePath));
            lyrics = _sngImporter.ImportLyrics(decoded, source.ArchivePath);
        }
        else
        {
            lyrics = _xmlImporter.Import(payload);
        }

        if (lyrics.Lyrics.Count == 0)
        {
            throw new InvalidDataException("The vocal chart contains no lyric entries.");
        }

        return lyrics;
    }
}
