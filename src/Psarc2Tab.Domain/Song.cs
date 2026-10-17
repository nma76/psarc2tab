namespace Psarc2Tab.Domain;

/// <summary>Represents a song and its tablature arrangements.</summary>
public sealed class Song
{
    /// <summary>Creates a song with its available tracks.</summary>
    /// <param name="title">The display title.</param>
    /// <param name="artist">The artist name, or an empty string when unknown.</param>
    /// <param name="tracks">The song's tracks in presentation order.</param>
    /// <param name="sections">Optional section markers in source order.</param>
    /// <param name="centOffsetCents">The arrangement's pitch offset from A4=440 Hz, in cents.</param>
    /// <param name="beats">Optional beat markers in chronological order.</param>
    /// <exception cref="ArgumentException">The title is blank, no tracks are supplied, or beats are unordered.</exception>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public Song(string title, string artist, IEnumerable<Track> tracks, IEnumerable<SongSection>? sections = null,
        decimal centOffsetCents = 0, IEnumerable<BeatMarker>? beats = null)
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
        var sectionArray = (sections ?? []).ToArray();
        if (sectionArray.Any(section => section is null))
        {
            throw new ArgumentException("A song cannot contain a null section.", nameof(sections));
        }

        Sections = Array.AsReadOnly(sectionArray);
        CentOffsetCents = centOffsetCents;
        var beatArray = (beats ?? []).ToArray();
        if (beatArray.Any(beat => beat is null))
        {
            throw new ArgumentException("A song cannot contain a null beat marker.", nameof(beats));
        }

        if (beatArray.Zip(beatArray.Skip(1)).Any(pair => pair.First.Time >= pair.Second.Time))
        {
            throw new ArgumentException("Beat markers must be in strictly increasing time order.", nameof(beats));
        }

        Beats = Array.AsReadOnly(beatArray);
    }

    /// <summary>Gets the display title.</summary>
    public string Title { get; }

    /// <summary>Gets the artist name, which may be empty when unknown.</summary>
    public string Artist { get; }

    /// <summary>Gets the tracks in presentation order.</summary>
    public IReadOnlyList<Track> Tracks { get; }

    /// <summary>Gets the optional section markers in source order.</summary>
    public IReadOnlyList<SongSection> Sections { get; }

    /// <summary>Gets the arrangement's pitch offset from A4=440 Hz, in cents.</summary>
    public decimal CentOffsetCents { get; }

    /// <summary>Gets source beat markers in chronological order.</summary>
    public IReadOnlyList<BeatMarker> Beats { get; }
}
