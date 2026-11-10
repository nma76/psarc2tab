namespace Psarc2Tab.Application;

/// <summary>Coordinates archive import and feedpak package writing.</summary>
public sealed class ExportFeedpakUseCase
{
    /// <summary>Imports feedpak source data from an archive.</summary>
    private readonly IArchiveFeedpakImporter _importer;

    /// <summary>Writes package data as a feedpak archive.</summary>
    private readonly IFeedpakPackageWriter _writer;

    /// <summary>Creates the feedpak export use case from its source adapter and writer.</summary>
    /// <param name="importer">The archive-specific feedpak data importer.</param>
    /// <param name="writer">The feedpak package writer.</param>
    /// <exception cref="ArgumentNullException">A required dependency is null.</exception>
    public ExportFeedpakUseCase(IArchiveFeedpakImporter importer, IFeedpakPackageWriter writer)
    {
        ArgumentNullException.ThrowIfNull(importer);
        ArgumentNullException.ThrowIfNull(writer);
        _importer = importer;
        _writer = writer;
    }

    /// <summary>Imports a PSARC and writes one feedpak to the destination.</summary>
    /// <param name="archive">The readable, seekable PSARC archive.</param>
    /// <param name="destination">The writable ZIP destination.</param>
    public void Execute(Stream archive, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(destination);
        var package = _importer.Import(archive);
        _writer.Write(package, destination);
    }
}
