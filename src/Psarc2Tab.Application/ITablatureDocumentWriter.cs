using Psarc2Tab.Domain;

namespace Psarc2Tab.Application;

/// <summary>Writes a selected domain track to a tablature document.</summary>
public interface ITablatureDocumentWriter
{
    /// <summary>Writes the tablature for a track belonging to the supplied song.</summary>
    /// <param name="song">The song that owns the track.</param>
    /// <param name="track">The selected track to write.</param>
    /// <param name="destination">The destination text writer.</param>
    void Write(Song song, Track track, TextWriter destination);
}
