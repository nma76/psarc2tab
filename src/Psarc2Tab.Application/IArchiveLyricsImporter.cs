using Psarc2Tab.Domain;

namespace Psarc2Tab.Application;

/// <summary>Imports timed lyrics from a supported song archive.</summary>
public interface IArchiveLyricsImporter
{
    /// <summary>Reads the lyrics stored in an archive.</summary>
    /// <param name="archive">The readable, seekable archive stream.</param>
    /// <returns>The imported timed lyrics.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="archive"/> is null.</exception>
    /// <exception cref="InvalidDataException">The archive or its lyrics are malformed or unsupported.</exception>
    SongLyrics Import(Stream archive);
}
