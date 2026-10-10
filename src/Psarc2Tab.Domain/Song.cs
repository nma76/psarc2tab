namespace Psarc2Tab.Domain;

/// <summary>Represents a song and its tablature arrangements.</summary>
public sealed class Song
{
    /// <summary>Creates a song with its available tracks.</summary>
    /// <param name="title">The display title.</param>
    /// <param name="artist">The artist name, or an empty string when unknown.</param>
    /// <param name="tracks">The song's tracks in presentation order.</param>
    /// <exception cref="ArgumentException">The title is blank or no tracks are supplied.</exception>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public Song(string title, string artist, IEnumerable<Track> tracks)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(artist);
        ArgumentNullException.ThrowIfNull(tracks);

        var trackArray = tracks.ToArray();
        if (trackArray.Length == 0)
        {
            throw new ArgumentException("A song must contain at least one track.", nameof(tracks));
        }

        if (trackArray.Any(track => track is null))
        {
            throw new ArgumentException("A song cannot contain a null track.", nameof(tracks));
        }

        Title = title;
        Artist = artist;
        Tracks = Array.AsReadOnly(trackArray);
    }

    /// <summary>Gets the display title.</summary>
    public string Title { get; }

    /// <summary>Gets the artist name, which may be empty when unknown.</summary>
    public string Artist { get; }

    /// <summary>Gets the tracks in presentation order.</summary>
    public IReadOnlyList<Track> Tracks { get; }
}
