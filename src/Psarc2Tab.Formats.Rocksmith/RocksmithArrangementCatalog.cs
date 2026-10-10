using System.Text;
using System.Security.Cryptography;
using Psarc2Tab.Formats.PSARC;

namespace Psarc2Tab.Formats.Rocksmith;

/// <summary>Lists supported instrument arrangements by resolving archive paths from the manifest.</summary>
public sealed class RocksmithArrangementCatalog
{
    /// <summary>The largest manifest accepted for arrangement discovery.</summary>
    private const ulong MaximumManifestLength = 4 * 1024 * 1024;

    /// <summary>The largest arrangement XML entry accepted for import.</summary>
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

    /// <summary>Extracts one numbered arrangement XML entry to a writable stream.</summary>
    /// <param name="archiveStream">The readable, seekable PSARC archive.</param>
    /// <param name="arrangementNumber">The one-based number from <see cref="List(Stream)"/>.</param>
    /// <param name="destination">The writable stream receiving the XML bytes.</param>
    /// <returns>The selected arrangement metadata.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentException">A stream does not support the required operations.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The number is not present in the archive listing.</exception>
    /// <exception cref="InvalidDataException">The archive is malformed or the XML entry exceeds its size limit.</exception>
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
            throw new InvalidDataException("The arrangement XML exceeds the supported 64 MiB size limit.");
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
        }

        if (arrangements.Count == 0)
        {
            throw new InvalidDataException("The PSARC manifest contains no supported instrument arrangements.");
        }

        return new ParsedRocksmithArchive(header, index, arrangements.ToArray());
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
        SupportedArrangement[] Arrangements);
}
