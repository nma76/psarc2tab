using Psarc2Tab.Domain;

namespace Psarc2Tab.Application;

/// <summary>Imports one Rocksmith arrangement document into the song domain model.</summary>
public interface IRocksmithArrangementImporter
{
    /// <summary>Reads one arrangement XML document from a stream.</summary>
    /// <param name="source">The readable arrangement XML stream.</param>
    /// <returns>The song containing the imported arrangement.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    /// <exception cref="ArgumentException">The stream is not readable.</exception>
    /// <exception cref="InvalidDataException">The XML is malformed, unsupported, or inconsistent.</exception>
    Song Import(Stream source);

    /// <summary>Imports a document with the instrument established by its archive entry.</summary>
    /// <param name="source">The readable arrangement XML stream.</param>
    /// <param name="archiveInstrument">The instrument identified from the archive path.</param>
    /// <returns>The song containing the imported arrangement.</returns>
    Song Import(Stream source, string archiveInstrument) => Import(source);
}
