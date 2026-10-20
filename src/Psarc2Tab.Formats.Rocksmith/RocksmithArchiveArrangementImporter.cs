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

    /// <summary>Converts decoded SNG chart data into domain songs.</summary>
    private readonly SngChartImporter _sngImporter;

    /// <summary>Creates an archive importer from its catalog and XML parser.</summary>
    /// <param name="catalog">The PSARC arrangement catalog.</param>
    /// <param name="xmlImporter">The Rocksmith XML importer.</param>
    /// <exception cref="ArgumentNullException">A required adapter is null.</exception>
    public RocksmithArchiveArrangementImporter(
        RocksmithArrangementCatalog catalog,
        IRocksmithArrangementImporter xmlImporter,
        SngChartImporter? sngImporter = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(xmlImporter);
        _catalog = catalog;
        _xmlImporter = xmlImporter;
        _sngImporter = sngImporter ?? new SngChartImporter();
    }

    /// <inheritdoc />
    public Song Import(Stream archive, int arrangementNumber)
    {
        ArgumentNullException.ThrowIfNull(archive);
        using var arrangementPayload = new MemoryStream();
        RocksmithArrangement selected;
        try
        {
            selected = _catalog.Extract(archive, arrangementNumber, arrangementPayload);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new ArrangementNotFoundException(arrangementNumber, exception);
        }

        if (selected.DataFormat == RocksmithArrangementFormat.Sng)
        {
            arrangementPayload.Position = 0;
            var decodedChart = new SngContainerReader().Decode(arrangementPayload,
                SngContainerReader.GetPlatform(selected.ArchivePath));
            return _sngImporter.Import(decodedChart, selected);
        }

        arrangementPayload.Position = 0;
        var song = _xmlImporter.Import(arrangementPayload, selected.Instrument);
        if (song.Tracks.Count != 1 || !string.Equals(song.Tracks[0].Name, selected.Instrument, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"The selected '{selected.Instrument}' archive entry does not contain a matching arrangement.");
        }

        return song;
    }
}
