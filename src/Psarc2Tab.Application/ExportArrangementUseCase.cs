using Psarc2Tab.Domain;

namespace Psarc2Tab.Application;

/// <summary>Coordinates arrangement import and tablature writing for one archive selection.</summary>
public sealed class ExportArrangementUseCase
{
    /// <summary>Imports the selected arrangement from a PSARC archive.</summary>
    private readonly IArchiveArrangementImporter _arrangementImporter;

    /// <summary>Writes domain tracks in the supported tablature format.</summary>
    private readonly ITablatureDocumentWriter _tablatureWriter;

    /// <summary>Creates an export use case from its format and output adapters.</summary>
    /// <param name="arrangementImporter">The archive arrangement importer.</param>
    /// <param name="tablatureWriter">The tablature document writer.</param>
    /// <exception cref="ArgumentNullException">A required adapter is null.</exception>
    public ExportArrangementUseCase(IArchiveArrangementImporter arrangementImporter, ITablatureDocumentWriter tablatureWriter)
    {
        ArgumentNullException.ThrowIfNull(arrangementImporter);
        ArgumentNullException.ThrowIfNull(tablatureWriter);
        _arrangementImporter = arrangementImporter;
        _tablatureWriter = tablatureWriter;
    }

    /// <summary>Imports one archive arrangement and writes its tablature document.</summary>
    /// <param name="archive">The readable, seekable PSARC archive stream.</param>
    /// <param name="arrangementNumber">The one-based number from the archive listing.</param>
    /// <param name="destination">The destination text writer.</param>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="InvalidOperationException">The importer returned anything other than one track.</exception>
    public void Execute(Stream archive, int arrangementNumber, TextWriter destination)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(destination);

        var song = Import(archive, arrangementNumber);
        Write(song, destination);
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
    /// <param name="destination">The destination text writer.</param>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="InvalidOperationException">The song does not contain exactly one track.</exception>
    public void Write(Song song, TextWriter destination)
    {
        ArgumentNullException.ThrowIfNull(song);
        ArgumentNullException.ThrowIfNull(destination);
        if (song.Tracks.Count != 1)
        {
            throw new InvalidOperationException("An imported arrangement must contain exactly one track.");
        }

        _tablatureWriter.Write(song, song.Tracks[0], destination);
    }
}
