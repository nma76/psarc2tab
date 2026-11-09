using Psarc2Tab.Application;

namespace Psarc2Tab.Formats.Rocksmith;

/// <summary>Extracts and decodes the complete Wwise mix from a single-song Rocksmith archive.</summary>
public sealed class RocksmithArchiveAudioImporter
{
    /// <summary>Limits WEM candidates to supported game-audio entries.</summary>
    private const ulong MaximumWemEntryLength = 64 * 1024 * 1024;

    /// <summary>Reads archive paths and extracts selected WEM entries.</summary>
    private readonly RocksmithArrangementCatalog _catalog;

    /// <summary>Decodes the selected WEM payload to PCM WAV.</summary>
    private readonly IWwiseAudioDecoder _decoder;

    /// <summary>Creates the archive audio importer.</summary>
    /// <param name="catalog">The validated Rocksmith archive catalog.</param>
    /// <param name="decoder">The Wwise audio decoder.</param>
    /// <exception cref="ArgumentNullException">A required dependency is null.</exception>
    public RocksmithArchiveAudioImporter(RocksmithArrangementCatalog catalog, IWwiseAudioDecoder decoder)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(decoder);
        _catalog = catalog;
        _decoder = decoder;
    }

    /// <summary>Imports the archive's full mix by distinguishing its larger WEM from its preview.</summary>
    /// <param name="archiveStream">The readable, seekable single-song PSARC archive.</param>
    /// <returns>The decoded PCM WAV mix.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="archiveStream"/> is null.</exception>
    /// <exception cref="InvalidDataException">The archive has no WEM or its candidates are ambiguous.</exception>
    public byte[] Import(Stream archiveStream)
    {
        ArgumentNullException.ThrowIfNull(archiveStream);
        if (!archiveStream.CanRead || !archiveStream.CanSeek)
        {
            throw new ArgumentException("A PSARC audio source must be readable and seekable.", nameof(archiveStream));
        }

        var archiveStart = archiveStream.Position;
        var details = _catalog.Inspect(archiveStream);
        var candidates = details.Entries
            .Where(entry => entry.Path.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) &&
                            entry.Path.EndsWith(".wem", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(entry => entry.UncompressedLength)
            .ToArray();
        if (candidates.Length is < 1 or > 2 || candidates.Any(entry => entry.UncompressedLength is 0 or > MaximumWemEntryLength))
        {
            throw new InvalidDataException("The archive does not contain an unambiguous, supported single-song WEM audio set.");
        }

        if (candidates.Length == 2 && candidates[0].UncompressedLength < candidates[1].UncompressedLength * 2)
        {
            throw new InvalidDataException("The archive's WEM entries cannot be safely distinguished as full audio and preview.");
        }

        archiveStream.Position = archiveStart;
        using var wemStream = new MemoryStream(checked((int)candidates[0].UncompressedLength));
        _catalog.ExtractEntry(archiveStream, candidates[0].Path, wemStream);
        return _decoder.DecodeToWave(wemStream.ToArray());
    }
}
