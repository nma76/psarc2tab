namespace Psarc2Tab.Application;

/// <summary>Imports all data needed to write a feedpak from one song archive.</summary>
public interface IArchiveFeedpakImporter
{
    /// <summary>Imports the archive's arrangements, optional lyrics, and complete WAV mix.</summary>
    /// <param name="archive">The readable, seekable PSARC archive.</param>
    /// <returns>The data for one feedpak package.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="archive"/> is null.</exception>
    /// <exception cref="InvalidDataException">The archive cannot be represented as one feedpak song.</exception>
    FeedpakExportPackage Import(Stream archive);
}
