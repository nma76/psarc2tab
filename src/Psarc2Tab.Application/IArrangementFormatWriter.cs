using Psarc2Tab.Domain;

namespace Psarc2Tab.Application;

/// <summary>Writes an arrangement in one named export format.</summary>
public interface IArrangementFormatWriter
{
    /// <summary>Gets the command-line name of this format.</summary>
    string FormatName { get; }

    /// <summary>Writes the selected arrangement to a stream.</summary>
    /// <param name="song">The song that owns the track.</param>
    /// <param name="track">The selected track.</param>
    /// <param name="destination">The destination stream.</param>
    void Write(Song song, Track track, Stream destination);
}
