using Psarc2Tab.Domain;

namespace Psarc2Tab.Application;

/// <summary>Imports one numbered instrument arrangement from a PSARC archive.</summary>
public interface IArchiveArrangementImporter
{
    /// <summary>Imports the selected arrangement from a readable archive stream.</summary>
    /// <param name="archive">The readable, seekable PSARC archive stream.</param>
    /// <param name="arrangementNumber">The one-based number from the archive arrangement listing.</param>
    /// <returns>The song containing the selected arrangement.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="archive"/> is null.</exception>
    /// <exception cref="ArrangementNotFoundException">The requested arrangement is unavailable.</exception>
    /// <exception cref="InvalidDataException">The archive or arrangement data is malformed or unsupported.</exception>
    Song Import(Stream archive, int arrangementNumber);
}
