using Psarc2Tab.Domain;

namespace Psarc2Tab.Application;

/// <summary>Contains the song arrangements, optional lyrics, and complete WAV mix for one feedpak.</summary>
public sealed class FeedpakExportPackage
{
    /// <summary>Creates a package from one song's arrangements and its complete WAV mix.</summary>
    /// <param name="arrangements">One single-track song model per arrangement, in archive order.</param>
    /// <param name="audioWave">A complete PCM WAV mix for the song.</param>
    /// <param name="lyrics">Optional timed lyrics for the song.</param>
    /// <param name="timeline">The shared song timeline selected from its arrangement charts.</param>
    /// <exception cref="ArgumentNullException">A required value is null.</exception>
    /// <exception cref="ArgumentException">The package is incomplete or the audio has no RIFF/WAVE header.</exception>
    public FeedpakExportPackage(IEnumerable<Song> arrangements, ReadOnlyMemory<byte> audioWave,
        SongLyrics? lyrics = null, Song? timeline = null)
    {
        ArgumentNullException.ThrowIfNull(arrangements);
        if (audioWave.Length < 12 || !audioWave.Span[..4].SequenceEqual("RIFF"u8) ||
            !audioWave.Span[8..12].SequenceEqual("WAVE"u8))
        {
            throw new ArgumentException("A feedpak package requires a complete PCM WAV audio mix.", nameof(audioWave));
        }

        var songArray = arrangements.ToArray();
        if (songArray.Length == 0 || songArray.Any(song => song is null || song.Tracks.Count != 1))
        {
            throw new ArgumentException("A feedpak package requires one or more single-track arrangements.", nameof(arrangements));
        }

        var firstSong = songArray[0];
        if (songArray.Any(song => !string.Equals(song.Title, firstSong.Title, StringComparison.Ordinal) ||
                                  !string.Equals(song.Artist, firstSong.Artist, StringComparison.Ordinal)))
        {
            throw new ArgumentException("All arrangements in a feedpak package must describe the same song.", nameof(arrangements));
        }

        Arrangements = Array.AsReadOnly(songArray);
        AudioWave = audioWave.ToArray();
        Lyrics = lyrics;
        Timeline = timeline ?? firstSong;
        if (!string.Equals(Timeline.Title, firstSong.Title, StringComparison.Ordinal) ||
            !string.Equals(Timeline.Artist, firstSong.Artist, StringComparison.Ordinal))
        {
            throw new ArgumentException("The package timeline must describe the same song as its arrangements.", nameof(timeline));
        }
    }

    /// <summary>Gets the arrangement songs in their archive order.</summary>
    public IReadOnlyList<Song> Arrangements { get; }

    /// <summary>Gets the shared song metadata and timeline from the first arrangement.</summary>
    public Song Song => Arrangements[0];

    /// <summary>Gets the combined song-wide beats and sections selected from imported charts.</summary>
    public Song Timeline { get; }

    /// <summary>Gets the complete PCM WAV mix.</summary>
    public ReadOnlyMemory<byte> AudioWave { get; }

    /// <summary>Gets the optional timed lyrics.</summary>
    public SongLyrics? Lyrics { get; }
}
