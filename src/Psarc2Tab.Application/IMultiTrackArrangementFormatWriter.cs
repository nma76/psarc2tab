using Psarc2Tab.Domain;

namespace Psarc2Tab.Application;

/// <summary>Writes several single-track arrangements into one multi-track file.</summary>
public interface IMultiTrackArrangementFormatWriter : IArrangementFormatWriter
{
    /// <summary>Writes each song's sole arrangement track to one destination file.</summary>
    /// <param name="songs">The arrangements in their required output track order.</param>
    /// <param name="destination">The writable destination stream.</param>
    /// <exception cref="ArgumentException">An arrangement does not contain exactly one track.</exception>
    /// <exception cref="InvalidDataException">The arrangements cannot share one MIDI timeline.</exception>
    void WriteMultiple(IReadOnlyList<Song> songs, Stream destination);
}
