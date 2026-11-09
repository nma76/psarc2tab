namespace Psarc2Tab.Application;

/// <summary>Writes one complete feedpak song package.</summary>
public interface IFeedpakPackageWriter
{
    /// <summary>Writes the package files to a feedpak ZIP stream.</summary>
    /// <param name="package">The validated package data.</param>
    /// <param name="destination">The writable, seekable destination stream.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">The destination is not writable.</exception>
    void Write(FeedpakExportPackage package, Stream destination);
}
