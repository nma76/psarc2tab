using Psarc2Tab.Application;
using Psarc2Tab.Domain;

namespace Psarc2Tab.Formats.Rocksmith;

/// <summary>Extracts and imports a selected arrangement from a supported PSARC archive.</summary>
public sealed class RocksmithArchiveArrangementImporter : IArchiveArrangementImporter
{
    /// <summary>Discovers and extracts arrangement entries from an archive.</summary>
    private readonly RocksmithArrangementCatalog _catalog;

    /// <summary>Converts extracted Rocksmith XML into domain songs.</summary>
    private readonly IRocksmithArrangementImporter _xmlImporter;

    /// <summary>Creates an archive importer from its catalog and XML parser.</summary>
    /// <param name="catalog">The PSARC arrangement catalog.</param>
    /// <param name="xmlImporter">The Rocksmith XML importer.</param>
    /// <exception cref="ArgumentNullException">A required adapter is null.</exception>
    public RocksmithArchiveArrangementImporter(
        RocksmithArrangementCatalog catalog,
        IRocksmithArrangementImporter xmlImporter)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(xmlImporter);
        _catalog = catalog;
        _xmlImporter = xmlImporter;
    }

    /// <inheritdoc />
    public Song Import(Stream archive, int arrangementNumber)
    {
        ArgumentNullException.ThrowIfNull(archive);
        using var arrangementXml = new MemoryStream();
        RocksmithArrangement selected;
        try
        {
            selected = _catalog.Extract(archive, arrangementNumber, arrangementXml);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new ArrangementNotFoundException(arrangementNumber, exception);
        }

        if (selected.DataFormat != RocksmithArrangementFormat.Xml)
        {
            throw new InvalidDataException(
                $"The selected '{selected.Instrument}' arrangement uses an SNG chart, which is not supported for conversion yet.");
        }

        arrangementXml.Position = 0;
        var song = _xmlImporter.Import(arrangementXml);
        if (song.Tracks.Count != 1 || !string.Equals(song.Tracks[0].Name, selected.Instrument, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"The selected '{selected.Instrument}' archive entry does not contain a matching arrangement.");
        }

        return song;
    }
}
