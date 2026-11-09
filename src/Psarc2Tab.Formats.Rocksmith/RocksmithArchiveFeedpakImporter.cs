using Psarc2Tab.Application;
using Psarc2Tab.Domain;

namespace Psarc2Tab.Formats.Rocksmith;

/// <summary>Collects supported chart, lyric, and audio data for a single-song feedpak package.</summary>
public sealed class RocksmithArchiveFeedpakImporter : IArchiveFeedpakImporter
{
    /// <summary>Finds the archive's arrangements and archive entries.</summary>
    private readonly RocksmithArrangementCatalog _catalog;

    /// <summary>Imports one selected archive arrangement.</summary>
    private readonly IArchiveArrangementImporter _arrangementImporter;

    /// <summary>Imports timed lyrics when the archive has a vocals chart.</summary>
    private readonly IArchiveLyricsImporter _lyricsImporter;

    /// <summary>Extracts and decodes the archive's complete mix.</summary>
    private readonly RocksmithArchiveAudioImporter _audioImporter;

    /// <summary>Creates the archive adapter used by feedpak export.</summary>
    /// <param name="catalog">The archive arrangement catalog.</param>
    /// <param name="arrangementImporter">The arrangement importer.</param>
    /// <param name="lyricsImporter">The lyric importer.</param>
    /// <param name="audioImporter">The Wwise audio importer.</param>
    public RocksmithArchiveFeedpakImporter(RocksmithArrangementCatalog catalog,
        IArchiveArrangementImporter arrangementImporter, IArchiveLyricsImporter lyricsImporter,
        RocksmithArchiveAudioImporter audioImporter)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(arrangementImporter);
        ArgumentNullException.ThrowIfNull(lyricsImporter);
        ArgumentNullException.ThrowIfNull(audioImporter);
        _catalog = catalog;
        _arrangementImporter = arrangementImporter;
        _lyricsImporter = lyricsImporter;
        _audioImporter = audioImporter;
    }

    /// <inheritdoc />
    public FeedpakExportPackage Import(Stream archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        if (!archive.CanRead || !archive.CanSeek)
        {
            throw new ArgumentException("Feedpak import requires a readable, seekable PSARC archive.", nameof(archive));
        }

        var archiveStart = archive.Position;
        var items = _catalog.ListItems(archive).Where(item => !item.IsLyrics).ToArray();
        var songs = new List<Song>(items.Length);
        foreach (var item in items)
        {
            archive.Position = archiveStart;
            songs.Add(_arrangementImporter.Import(archive, item.Number));
        }

        ValidateOneSong(songs);
        var timeline = BuildSharedTimeline(songs);
        archive.Position = archiveStart;
        SongLyrics? lyrics = null;
        if (_catalog.ListItems(archive).Any(item => item.IsLyrics))
        {
            archive.Position = archiveStart;
            lyrics = _lyricsImporter.Import(archive);
        }

        archive.Position = archiveStart;
        var audio = _audioImporter.Import(archive);
        return new FeedpakExportPackage(songs, audio, lyrics, timeline);
    }

    /// <summary>Rejects compilation archives until charts, lyrics, and audio can be joined per song.</summary>
    /// <param name="songs">The imported arrangement songs in archive order.</param>
    /// <exception cref="InvalidDataException">The archive contains multiple songs or inconsistent timelines.</exception>
    private static void ValidateOneSong(IReadOnlyList<Song> songs)
    {
        if (songs.Count == 0)
        {
            throw new InvalidDataException("The archive contains no instrument arrangements to export as a feedpak.");
        }

        var first = songs[0];
        if (songs.Any(song => !string.Equals(song.Title, first.Title, StringComparison.Ordinal) ||
                              !string.Equals(song.Artist, first.Artist, StringComparison.Ordinal)))
        {
            throw new InvalidDataException("This PSARC contains multiple songs; per-song feedpak grouping is not supported yet.");
        }

    }

    /// <summary>Combines song-wide beats and sections from the charts with the most complete data.</summary>
    /// <param name="songs">The imported arrangement songs for one recording.</param>
    /// <returns>A timeline carrying the richest available beat map and section list.</returns>
    private static Song BuildSharedTimeline(IReadOnlyList<Song> songs)
    {
        var first = songs[0];
        var beatSource = songs.OrderByDescending(song => song.Beats.Count).First();
        var sectionSource = songs.OrderByDescending(song => song.Sections.Count).First();
        return new Song(first.Title, first.Artist, first.Tracks, sectionSource.Sections,
            first.CentOffsetCents, beatSource.Beats, first.AlbumName, first.AlbumYear);
    }
}
