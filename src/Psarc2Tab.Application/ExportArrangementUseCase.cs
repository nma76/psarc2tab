using Psarc2Tab.Domain;

namespace Psarc2Tab.Application;

/// <summary>Coordinates arrangement import and format-specific writing for one archive selection.</summary>
public sealed class ExportArrangementUseCase
{
    /// <summary>Imports a selected arrangement from an archive.</summary>
    private readonly IArchiveArrangementImporter _arrangementImporter;

    /// <summary>Resolves writers for supported export formats.</summary>
    private readonly ArrangementFormatRegistry _formatRegistry;

    /// <summary>Creates an export use case from its importer and format writers.</summary>
    /// <param name="arrangementImporter">The archive arrangement importer.</param>
    /// <param name="formatRegistry">The registry of arrangement output formats.</param>
    /// <exception cref="ArgumentNullException">A required adapter is null.</exception>
    public ExportArrangementUseCase(IArchiveArrangementImporter arrangementImporter,
        ArrangementFormatRegistry formatRegistry)
    {
        ArgumentNullException.ThrowIfNull(arrangementImporter);
        ArgumentNullException.ThrowIfNull(formatRegistry);
        _arrangementImporter = arrangementImporter;
        _formatRegistry = formatRegistry;
    }

    /// <summary>Gets the supported arrangement output format names.</summary>
    public IReadOnlyList<string> SupportedFormatNames => _formatRegistry.SupportedFormatNames;

    /// <summary>Checks whether an arrangement output format is registered.</summary>
    /// <param name="formatName">The requested format name.</param>
    /// <returns><see langword="true"/> when the format has a writer.</returns>
    public bool SupportsFormat(string formatName) => _formatRegistry.TryGetWriter(formatName, out _);

    /// <summary>Imports one archive arrangement and writes it in the requested format.</summary>
    /// <param name="archive">The readable, seekable PSARC archive stream.</param>
    /// <param name="arrangementNumber">The one-based number from the archive listing.</param>
    /// <param name="formatName">The registered output format.</param>
    /// <param name="destination">The destination stream.</param>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="InvalidOperationException">The importer returned anything other than one track.</exception>
    public void Execute(Stream archive, int arrangementNumber, string formatName, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(formatName);
        ArgumentNullException.ThrowIfNull(destination);
        if (!SupportsFormat(formatName))
        {
            throw new ArgumentException($"The '{formatName}' output format is not supported.", nameof(formatName));
        }

        var song = Import(archive, arrangementNumber);
        Write(song, formatName, destination);
    }

    /// <summary>Imports one arrangement and validates its single-track result.</summary>
    /// <param name="archive">The readable, seekable PSARC archive stream.</param>
    /// <param name="arrangementNumber">The one-based number from the archive listing.</param>
    /// <returns>The imported song with its selected track.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="archive"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The importer returned anything other than one track.</exception>
    public Song Import(Stream archive, int arrangementNumber)
    {
        ArgumentNullException.ThrowIfNull(archive);
        var song = _arrangementImporter.Import(archive, arrangementNumber);
        if (song.Tracks.Count != 1)
        {
            throw new InvalidOperationException("An arrangement importer must return a song with exactly one track.");
        }

        return song;
    }

    /// <summary>Writes the one selected track in an imported arrangement.</summary>
    /// <param name="song">The imported song.</param>
    /// <param name="formatName">The registered output format.</param>
    /// <param name="destination">The destination stream.</param>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="InvalidOperationException">The song does not contain exactly one track.</exception>
    /// <exception cref="ArgumentException">The requested format is not registered.</exception>
    public void Write(Song song, string formatName, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(song);
        ArgumentNullException.ThrowIfNull(formatName);
        ArgumentNullException.ThrowIfNull(destination);
        if (song.Tracks.Count != 1)
        {
            throw new InvalidOperationException("An imported arrangement must contain exactly one track.");
        }

        if (!_formatRegistry.TryGetWriter(formatName, out var writer) || writer is null)
        {
            throw new ArgumentException($"The '{formatName}' output format is not supported.", nameof(formatName));
        }

        writer.Write(song, song.Tracks[0], destination);
    }
}
