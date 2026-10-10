using System.Text;
using System.Security.Cryptography;
using Psarc2Tab.Formats.PSARC;

namespace Psarc2Tab.Formats.Rocksmith;

/// <summary>Lists supported instrument arrangements by resolving archive paths from the manifest.</summary>
public sealed class RocksmithArrangementCatalog
{
    /// <summary>The largest manifest accepted for arrangement discovery.</summary>
    private const ulong MaximumManifestLength = 4 * 1024 * 1024;

    /// <summary>Reads arrangement paths from a supported PSARC stream.</summary>
    /// <param name="archiveStream">The readable, seekable PSARC archive.</param>
    /// <returns>Instrument arrangements in manifest order, numbered from one.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="archiveStream"/> is null.</exception>
    /// <exception cref="ArgumentException">The archive stream is not readable and seekable.</exception>
    /// <exception cref="InvalidDataException">The PSARC or its manifest is malformed or unsupported.</exception>
    public IReadOnlyList<RocksmithArrangement> List(Stream archiveStream)
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

        var arrangements = new List<RocksmithArrangement>();
        foreach (var path in paths)
        {
            if (TryGetInstrument(path, out var instrument))
            {
                arrangements.Add(new RocksmithArrangement(arrangements.Count + 1, instrument, path));
            }
        }

        if (arrangements.Count == 0)
        {
            throw new InvalidDataException("The PSARC manifest contains no supported instrument arrangements.");
        }

        return arrangements.AsReadOnly();
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
}
