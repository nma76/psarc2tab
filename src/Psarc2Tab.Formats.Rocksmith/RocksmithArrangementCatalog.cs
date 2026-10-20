using System.Text;
using System.Security.Cryptography;
using System.Text.Json;
using Psarc2Tab.Formats.PSARC;

namespace Psarc2Tab.Formats.Rocksmith;

/// <summary>Lists supported instrument arrangements by resolving archive paths from the manifest.</summary>
public sealed class RocksmithArrangementCatalog
{
    /// <summary>The largest manifest accepted for arrangement discovery.</summary>
    private const ulong MaximumManifestLength = 4 * 1024 * 1024;

    /// <summary>The largest arrangement chart entry accepted for extraction.</summary>
    private const ulong MaximumArrangementLength = 64 * 1024 * 1024;

    /// <summary>Reads arrangement paths from a supported PSARC stream.</summary>
    /// <param name="archiveStream">The readable, seekable PSARC archive.</param>
    /// <returns>Instrument arrangements in manifest order, numbered from one.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="archiveStream"/> is null.</exception>
    /// <exception cref="ArgumentException">The archive stream is not readable and seekable.</exception>
    /// <exception cref="InvalidDataException">The PSARC or its manifest is malformed or unsupported.</exception>
    public IReadOnlyList<RocksmithArrangement> List(Stream archiveStream)
    {
        var archive = ReadArchive(archiveStream);
        return Array.AsReadOnly(archive.Arrangements.Select(item => item.Arrangement).ToArray());
    }

    /// <summary>Inspects archive metadata and manifest paths without extracting chart payloads.</summary>
    /// <param name="archiveStream">The readable, seekable PSARC archive.</param>
    /// <returns>The validated header and resolved archive paths.</returns>
    public RocksmithArchiveDetails Inspect(Stream archiveStream)
    {
        var archive = ReadArchive(archiveStream);
        return new RocksmithArchiveDetails(archive.Header, Array.AsReadOnly(archive.Paths));
    }

    /// <summary>Lists instrument arrangements and appends a lyric export when vocal data exists.</summary>
    /// <param name="archiveStream">The readable, seekable PSARC archive.</param>
    /// <returns>Selectable export items in archive arrangement order, with lyrics last when available.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="archiveStream"/> is null.</exception>
    /// <exception cref="InvalidDataException">The archive or its manifest is malformed or unsupported.</exception>
    public IReadOnlyList<RocksmithArchiveItem> ListItems(Stream archiveStream)
    {
        var archive = ReadArchive(archiveStream);
        var items = archive.Arrangements.Select((item, index) => new RocksmithArchiveItem(
            index + 1, item.Arrangement.Instrument, item.Arrangement.ArchivePath)).ToList();
        if (archive.LyricsSource is not null)
        {
            items.Add(new RocksmithArchiveItem(items.Count + 1, "Lyrics", archive.LyricsSource.ArchivePath, IsLyrics: true));
        }

        return Array.AsReadOnly(items.ToArray());
    }

    /// <summary>Extracts the vocal chart entry when the archive contains one.</summary>
    /// <param name="archiveStream">The readable, seekable PSARC archive.</param>
    /// <param name="destination">The writable stream receiving the vocal chart bytes.</param>
    /// <returns>The source path and format for the extracted chart.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">A stream does not have the required capabilities.</exception>
    /// <exception cref="InvalidDataException">The archive has no vocal chart or its entry is malformed.</exception>
    public RocksmithLyricsSource ExtractLyrics(Stream archiveStream, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite || ReferenceEquals(archiveStream, destination))
        {
            throw new ArgumentException("The lyric destination must be writable and separate from the archive stream.", nameof(destination));
        }

        var archive = ReadArchive(archiveStream);
        var source = archive.LyricsSource
            ?? throw new InvalidDataException("The PSARC archive does not contain a supported vocal chart.");
        var pathIndex = Array.IndexOf(archive.Paths, source.ArchivePath);
        if (pathIndex < 0)
        {
            throw new InvalidDataException("The vocal chart path is missing from the PSARC manifest.");
        }

        var entryIndex = pathIndex + 1;
        var entry = archive.Index.Entries[entryIndex];
        if (entry.UncompressedLength > MaximumArrangementLength)
        {
            throw new InvalidDataException("The vocal chart exceeds the supported 64 MiB size limit.");
        }

        PsarcEntryExtractor.Extract(archiveStream, archive.Header, archive.Index, entryIndex, destination);
        return source;
    }

    /// <summary>Extracts one numbered arrangement chart entry to a writable stream.</summary>
    /// <param name="archiveStream">The readable, seekable PSARC archive.</param>
    /// <param name="arrangementNumber">The one-based number from <see cref="List(Stream)"/>.</param>
    /// <param name="destination">The writable stream receiving the chart bytes.</param>
    /// <returns>The selected arrangement metadata.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentException">A stream does not support the required operations.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The number is not present in the archive listing.</exception>
    /// <exception cref="InvalidDataException">The archive is malformed or the chart entry exceeds its size limit.</exception>
    public RocksmithArrangement Extract(Stream archiveStream, int arrangementNumber, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
        {
            throw new ArgumentException("The arrangement destination must be writable.", nameof(destination));
        }

        if (ReferenceEquals(archiveStream, destination))
        {
            throw new ArgumentException("The archive stream cannot also be the arrangement destination.", nameof(destination));
        }

        var archive = ReadArchive(archiveStream);
        if (arrangementNumber <= 0 || arrangementNumber > archive.Arrangements.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(arrangementNumber), arrangementNumber,
                "The arrangement number is outside the archive listing.");
        }

        var selected = archive.Arrangements[arrangementNumber - 1];
        var entry = archive.Index.Entries[selected.EntryIndex];
        if (entry.UncompressedLength > MaximumArrangementLength)
        {
            throw new InvalidDataException("The arrangement chart exceeds the supported 64 MiB size limit.");
        }

        PsarcEntryExtractor.Extract(archiveStream, archive.Header, archive.Index, selected.EntryIndex, destination);
        return selected.Arrangement;
    }

    /// <summary>Reads the archive index, validates its manifest, and discovers supported arrangements.</summary>
    /// <param name="archiveStream">The readable, seekable PSARC archive.</param>
    /// <returns>The parsed archive metadata and arrangement entry positions.</returns>
    private static ParsedRocksmithArchive ReadArchive(Stream archiveStream)
    {
        ArgumentNullException.ThrowIfNull(archiveStream);
        if (!archiveStream.CanRead || !archiveStream.CanSeek)
        {
            throw new ArgumentException("A PSARC stream must be readable and seekable.", nameof(archiveStream));
        }

        var header = PsarcHeaderReader.Read(archiveStream);
        var index = PsarcIndexReader.Read(archiveStream, header);
        if (index.Entries.Count == 0)
        {
            throw new InvalidDataException("The PSARC archive has no manifest entry.");
        }

        var manifestEntry = index.Entries[0];
        if (manifestEntry.UncompressedLength > MaximumManifestLength)
        {
            throw new InvalidDataException("The PSARC manifest exceeds the supported 4 MiB size limit.");
        }

        using var manifestStream = new MemoryStream(checked((int)manifestEntry.UncompressedLength));
        PsarcEntryExtractor.Extract(archiveStream, header, index, 0, manifestStream);
        var paths = DecodeManifest(manifestStream.ToArray(), index.Entries.Count - 1);
        ValidatePathDigests(paths, index);

        var arrangements = new List<SupportedArrangement>();
        for (var pathIndex = 0; pathIndex < paths.Length; pathIndex++)
        {
            var path = paths[pathIndex];
            if (TryGetInstrument(path, out var instrument))
            {
                var listing = new RocksmithArrangement(arrangements.Count + 1, instrument, path);
                arrangements.Add(new SupportedArrangement(listing, pathIndex + 1));
            }
            else if (TryGetManifestInstrument(path, out instrument))
            {
                var metadata = ReadArrangementMetadata(archiveStream, header, index, pathIndex + 1, instrument);
                var xmlArrangementPath = $"songs/arr/{metadata.SongXmlAsset["urn:application:xml:".Length..]}.xml";
                if (paths.Contains(xmlArrangementPath, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var chartPath = FindSngChartPath(paths, metadata.SongXmlAsset, instrument);
                var chartEntryIndex = Array.IndexOf(paths, chartPath) + 1;
                var sngMetadata = new RocksmithSngMetadata(metadata.SongName, metadata.ArtistName,
                    Array.AsReadOnly(metadata.TuningOffsets), metadata.CentOffsetCents);
                var listing = new RocksmithArrangement(arrangements.Count + 1, instrument, chartPath,
                    RocksmithArrangementFormat.Sng, path, sngMetadata);
                arrangements.Add(new SupportedArrangement(listing, chartEntryIndex));
            }
        }

        if (arrangements.Count == 0)
        {
            throw new InvalidDataException("The PSARC manifest contains no supported instrument arrangements.");
        }

        var lyricSource = FindLyricsSource(paths, arrangements);
        return new ParsedRocksmithArchive(header, index, paths, arrangements.ToArray(), lyricSource);
    }

    /// <summary>Decodes the UTF-8 path manifest and checks its entry count.</summary>
    /// <param name="manifestBytes">The extracted manifest payload.</param>
    /// <param name="expectedPathCount">The number of non-manifest TOC entries.</param>
    /// <returns>Archive paths in their manifest order.</returns>
    /// <exception cref="InvalidDataException">The manifest encoding, lines, or path count is invalid.</exception>
    private static string[] DecodeManifest(byte[] manifestBytes, int expectedPathCount)
    {
        string manifest;
        try
        {
            manifest = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(manifestBytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("The PSARC manifest is not valid UTF-8.", exception);
        }

        var paths = manifest.Split('\n');
        if (paths.Length > 0 && paths[^1].Length == 0)
        {
            Array.Resize(ref paths, paths.Length - 1);
        }

        for (var index = 0; index < paths.Length; index++)
        {
            paths[index] = paths[index].TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(paths[index]))
            {
                throw new InvalidDataException($"The PSARC manifest contains an empty path at line {index + 1}.");
            }
        }

        if (paths.Length != expectedPathCount)
        {
            throw new InvalidDataException(
                $"The PSARC manifest contains {paths.Length} paths, but the index has {expectedPathCount} file entries.");
        }

        return paths;
    }

    /// <summary>Checks each manifest path against its corresponding TOC digest.</summary>
    /// <param name="paths">The decoded manifest paths.</param>
    /// <param name="index">The parsed archive index.</param>
    /// <exception cref="InvalidDataException">A path digest differs from the matching TOC entry.</exception>
    private static void ValidatePathDigests(string[] paths, PsarcArchiveIndex index)
    {
        for (var pathIndex = 0; pathIndex < paths.Length; pathIndex++)
        {
            var digest = MD5.HashData(Encoding.UTF8.GetBytes(paths[pathIndex]));
            if (!digest.AsSpan().SequenceEqual(index.Entries[pathIndex + 1].PathDigest.Span))
            {
                throw new InvalidDataException($"The PSARC manifest path at line {pathIndex + 1} does not match its index digest.");
            }
        }
    }

    /// <summary>Recognizes supported instrument XML files under the observed arrangement directory.</summary>
    /// <param name="path">A path from the PSARC manifest.</param>
    /// <param name="instrument">The recognized instrument label.</param>
    /// <returns><see langword="true"/> when the path names a supported instrument arrangement.</returns>
    private static bool TryGetInstrument(string path, out string instrument)
    {
        instrument = string.Empty;
        const string arrangementDirectory = "songs/arr/";
        if (!path.StartsWith(arrangementDirectory, StringComparison.OrdinalIgnoreCase) ||
            !path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var fileName = path[(path.LastIndexOf('/') + 1)..];
        var extension = fileName.LastIndexOf('.');
        if (extension >= 0)
        {
            fileName = fileName[..extension];
        }

        var separator = fileName.LastIndexOf('_');
        if (separator < 0 || separator == fileName.Length - 1)
        {
            return false;
        }

        var suffix = fileName[(separator + 1)..];
        instrument = suffix.ToLowerInvariant() switch
        {
            "lead" => "Lead",
            "bass" => "Bass",
            "rhythm" => "Rhythm",
            _ => string.Empty
        };

        return instrument.Length > 0;
    }

    /// <summary>Recognizes JSON manifest paths for supported instrument arrangements.</summary>
    /// <param name="path">A path from the PSARC manifest.</param>
    /// <param name="instrument">The recognized instrument label.</param>
    /// <returns><see langword="true"/> when the path is an arrangement JSON manifest.</returns>
    private static bool TryGetManifestInstrument(string path, out string instrument)
    {
        instrument = string.Empty;
        if (!path.StartsWith("manifests/", StringComparison.OrdinalIgnoreCase) ||
            !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var fileName = path[(path.LastIndexOf('/') + 1)..];
        var separator = fileName.LastIndexOf('_');
        if (separator < 0)
        {
            return false;
        }

        instrument = fileName[(separator + 1)..^5].ToLowerInvariant() switch
        {
            "lead" => "Lead",
            "bass" => "Bass",
            "rhythm" => "Rhythm",
            _ => string.Empty
        };
        return instrument.Length > 0;
    }

    /// <summary>Reads the SongXml asset identifier from an arrangement JSON manifest.</summary>
    /// <param name="archiveStream">The PSARC archive stream.</param>
    /// <param name="header">The parsed PSARC header.</param>
    /// <param name="index">The parsed PSARC index.</param>
    /// <param name="entryIndex">The index entry containing the JSON manifest.</param>
    /// <param name="expectedInstrument">The instrument inferred from the manifest path.</param>
    /// <returns>The XML asset identifier used to correlate its SNG chart.</returns>
    private static ArrangementMetadata ReadArrangementMetadata(Stream archiveStream, PsarcHeader header,
        PsarcArchiveIndex index, int entryIndex, string expectedInstrument)
    {
        var entry = index.Entries[entryIndex];
        if (entry.UncompressedLength > MaximumManifestLength)
        {
            throw new InvalidDataException("A Rocksmith arrangement JSON manifest exceeds the supported 4 MiB size limit.");
        }

        using var jsonStream = new MemoryStream(checked((int)entry.UncompressedLength));
        PsarcEntryExtractor.Extract(archiveStream, header, index, entryIndex, jsonStream);
        try
        {
            using var document = JsonDocument.Parse(jsonStream.ToArray());
            var entries = document.RootElement.GetProperty("Entries");
            var metadata = entries.EnumerateObject().Select(item => item.Value.GetProperty("Attributes")).Single();
            var instrument = metadata.GetProperty("ArrangementName").GetString();
            var songXmlAsset = metadata.GetProperty("SongXml").GetString();
            var songName = metadata.GetProperty("SongName").GetString();
            var artistName = metadata.GetProperty("ArtistName").GetString();
            var tuning = metadata.GetProperty("Tuning");
            var tuningOffsets = Enumerable.Range(0, 6)
                .Select(index => tuning.GetProperty($"string{index}").GetInt32()).ToArray();
            var centOffsetCents = metadata.TryGetProperty("CentOffset", out var centOffset)
                ? centOffset.GetDecimal()
                : 0;
            if (!string.Equals(instrument, expectedInstrument, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(songXmlAsset) || !songXmlAsset.StartsWith("urn:application:xml:", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(songName) || string.IsNullOrWhiteSpace(artistName))
            {
                throw new InvalidDataException("The Rocksmith arrangement JSON metadata is inconsistent with its manifest path.");
            }

            return new ArrangementMetadata(songXmlAsset, songName, artistName, tuningOffsets, centOffsetCents);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("A Rocksmith arrangement JSON manifest is malformed.", exception);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("A Rocksmith arrangement JSON manifest contains an invalid numeric value.", exception);
        }
        catch (KeyNotFoundException exception)
        {
            throw new InvalidDataException("A Rocksmith arrangement JSON manifest is missing required metadata.", exception);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidDataException("A Rocksmith arrangement JSON manifest has an unsupported structure.", exception);
        }
    }

    /// <summary>Finds the SNG entry whose basename corresponds to an arrangement's SongXml asset.</summary>
    /// <param name="paths">The archive paths from the top-level manifest.</param>
    /// <param name="songXmlAsset">The Rocksmith XML asset identifier.</param>
    /// <param name="instrument">The expected instrument label.</param>
    /// <returns>The matching chart path.</returns>
    private static string FindSngChartPath(string[] paths, string songXmlAsset, string instrument)
    {
        var assetName = songXmlAsset["urn:application:xml:".Length..];
        var expectedSuffix = $"_{instrument.ToLowerInvariant()}";
        if (!assetName.EndsWith(expectedSuffix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The Rocksmith arrangement asset name does not identify its instrument.");
        }

        var matches = paths.Where(path => path.StartsWith("songs/bin/", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(path[(path.LastIndexOf('/') + 1)..], assetName + ".sng", StringComparison.OrdinalIgnoreCase)).ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidDataException($"The SNG chart for the '{instrument}' arrangement is missing from the archive."),
            _ => throw new InvalidDataException($"The SNG chart for the '{instrument}' arrangement is ambiguous.")
        };
    }

    /// <summary>Finds one vocal chart matching the song key of a discovered instrument arrangement.</summary>
    /// <param name="paths">The archive paths from the top-level manifest.</param>
    /// <param name="arrangements">The discovered instrument arrangements.</param>
    /// <returns>The vocal chart source, or null when no matching vocal entry exists.</returns>
    /// <exception cref="InvalidDataException">More than one chart of the preferred format matches the song key.</exception>
    private static RocksmithLyricsSource? FindLyricsSource(string[] paths, IReadOnlyList<SupportedArrangement> arrangements)
    {
        var songKey = GetSongKey(arrangements[0].Arrangement.ArchivePath);
        if (songKey is null)
        {
            return null;
        }

        var xmlMatches = paths.Where(path => path.StartsWith("songs/arr/", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(GetFileName(path), $"{songKey}_vocals.xml", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (xmlMatches.Length > 1)
        {
            throw new InvalidDataException("The archive contains multiple XML vocal charts for the selected song.");
        }

        if (xmlMatches.Length == 1)
        {
            return new RocksmithLyricsSource(xmlMatches[0], RocksmithArrangementFormat.Xml);
        }

        var sngMatches = paths.Where(path => path.StartsWith("songs/bin/", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(GetFileName(path), $"{songKey}_vocals.sng", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (sngMatches.Length > 1)
        {
            throw new InvalidDataException("The archive contains multiple SNG vocal charts for the selected song.");
        }

        return sngMatches.Length == 1
            ? new RocksmithLyricsSource(sngMatches[0], RocksmithArrangementFormat.Sng)
            : null;
    }

    /// <summary>Extracts the song key from a supported instrument chart filename.</summary>
    /// <param name="archivePath">The instrument chart path.</param>
    /// <returns>The filename stem without the instrument suffix, or null when no known suffix exists.</returns>
    private static string? GetSongKey(string archivePath)
    {
        var fileName = GetFileName(archivePath);
        var extensionIndex = fileName.LastIndexOf('.');
        if (extensionIndex < 0)
        {
            return null;
        }

        var stem = fileName[..extensionIndex];
        var separator = stem.LastIndexOf('_');
        if (separator < 0 || stem[(separator + 1)..].ToLowerInvariant() is not ("lead" or "bass" or "rhythm"))
        {
            return null;
        }

        return stem[..separator];
    }

    /// <summary>Gets a filename from a normalized archive path.</summary>
    /// <param name="path">The slash-separated archive path.</param>
    /// <returns>The final path component.</returns>
    private static string GetFileName(string path) => path[(path.LastIndexOf('/') + 1)..];

    /// <summary>Stores identifying metadata from a JSON arrangement manifest.</summary>
    /// <param name="SongXmlAsset">The stable asset identifier used to locate the SNG chart.</param>
    private sealed record ArrangementMetadata(string SongXmlAsset, string SongName, string ArtistName,
        int[] TuningOffsets, decimal CentOffsetCents);

    /// <summary>Stores arrangement metadata with its corresponding PSARC TOC entry number.</summary>
    /// <param name="Arrangement">The user-visible arrangement information.</param>
    /// <param name="EntryIndex">The zero-based PSARC TOC entry number.</param>
    private sealed record SupportedArrangement(RocksmithArrangement Arrangement, int EntryIndex);

    /// <summary>Stores validated archive metadata used by listing and selected extraction.</summary>
    /// <param name="Header">The parsed PSARC header.</param>
    /// <param name="Index">The parsed PSARC index.</param>
    /// <param name="Arrangements">The supported arrangements in manifest order.</param>
    private sealed record ParsedRocksmithArchive(
        PsarcHeader Header,
        PsarcArchiveIndex Index,
        string[] Paths,
        SupportedArrangement[] Arrangements,
        RocksmithLyricsSource? LyricsSource);
}
