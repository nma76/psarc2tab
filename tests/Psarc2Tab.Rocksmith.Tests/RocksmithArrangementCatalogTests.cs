using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Psarc2Tab.Formats.Rocksmith;

namespace Psarc2Tab.Rocksmith.Tests;

/// <summary>Verifies arrangement discovery from a PSARC manifest.</summary>
public sealed class RocksmithArrangementCatalogTests
{
    /// <summary>Lists supported instrument arrangements and skips vocals and show-light data.</summary>
    [Fact]
    public void List_MultipleArrangements_ReturnsNumberedInstrumentsInManifestOrder()
    {
        var archive = CreateArchive(
        [
            "songs/arr/sample_vocals.xml",
            "songs/arr/sample_bass.xml",
            "songs/arr/sample_lead.xml",
            "songs/arr/sample_showlights.xml",
            "songs/arr/sample_rhythm.xml"
        ]);
        using var stream = new MemoryStream(archive);

        var arrangements = new RocksmithArrangementCatalog().List(stream);

        Assert.Collection(
            arrangements,
            arrangement => Assert.Equal(new RocksmithArrangement(1, "Bass", "songs/arr/sample_bass.xml"), arrangement),
            arrangement => Assert.Equal(new RocksmithArrangement(2, "Lead", "songs/arr/sample_lead.xml"), arrangement),
            arrangement => Assert.Equal(new RocksmithArrangement(3, "Rhythm", "songs/arr/sample_rhythm.xml"), arrangement));
    }

    /// <summary>Rejects a manifest whose path count differs from the archive index.</summary>
    [Fact]
    public void List_MismatchedManifestCount_ThrowsInvalidDataException()
    {
        var archive = CreateArchive(["songs/arr/sample_lead.xml"], manifestPaths: []);
        using var stream = new MemoryStream(archive);

        Assert.Throws<InvalidDataException>(() => new RocksmithArrangementCatalog().List(stream));
    }

    /// <summary>Rejects a manifest path that does not match its TOC digest.</summary>
    [Fact]
    public void List_MismatchedPathDigest_ThrowsInvalidDataException()
    {
        var archive = CreateArchive(
            ["songs/arr/sample_lead.xml"],
            manifestPaths: ["songs/arr/other_lead.xml"]);
        using var stream = new MemoryStream(archive);

        Assert.Throws<InvalidDataException>(() => new RocksmithArrangementCatalog().List(stream));
    }

    /// <summary>Builds an archive with a manifest and one raw block for each path.</summary>
    /// <param name="paths">The non-manifest archive paths.</param>
    /// <param name="manifestPaths">Optional paths to encode in the manifest.</param>
    /// <returns>A minimal unencrypted PSARC byte sequence.</returns>
    private static byte[] CreateArchive(string[] paths, string[]? manifestPaths = null)
    {
        const int headerSize = 32;
        const int entrySize = 30;
        var manifest = Encoding.UTF8.GetBytes(string.Join('\n', manifestPaths ?? paths));
        var content = new[] { manifest }.Concat(paths.Select(_ => "<song />"u8.ToArray())).ToArray();
        var tocSize = headerSize + content.Length * (entrySize + sizeof(ushort));
        var dataSize = content.Sum(entry => entry.Length);
        var archive = new byte[tocSize + dataSize];

        "PSAR"u8.CopyTo(archive);
        BinaryPrimitives.WriteUInt32BigEndian(archive.AsSpan(4), 0x0001_0004);
        "zlib"u8.CopyTo(archive.AsSpan(8));
        BinaryPrimitives.WriteUInt32BigEndian(archive.AsSpan(12), (uint)tocSize);
        BinaryPrimitives.WriteUInt32BigEndian(archive.AsSpan(16), entrySize);
        BinaryPrimitives.WriteUInt32BigEndian(archive.AsSpan(20), (uint)content.Length);
        BinaryPrimitives.WriteUInt32BigEndian(archive.AsSpan(24), 65_536);

        var blockTableOffset = headerSize + content.Length * entrySize;
        var dataOffset = tocSize;
        for (var entryIndex = 0; entryIndex < content.Length; entryIndex++)
        {
            var recordOffset = headerSize + entryIndex * entrySize;
            if (entryIndex > 0)
            {
                MD5.HashData(Encoding.UTF8.GetBytes(paths[entryIndex - 1]))
                    .CopyTo(archive.AsSpan(recordOffset, 16));
            }

            BinaryPrimitives.WriteUInt32BigEndian(archive.AsSpan(recordOffset + 16), (uint)entryIndex);
            WriteUInt40(archive.AsSpan(recordOffset + 20), (ulong)content[entryIndex].Length);
            WriteUInt40(archive.AsSpan(recordOffset + 25), (ulong)dataOffset);
            content[entryIndex].CopyTo(archive, dataOffset);
            dataOffset += content[entryIndex].Length;
            BinaryPrimitives.WriteUInt16BigEndian(archive.AsSpan(blockTableOffset + entryIndex * 2), 0);
        }

        return archive;
    }

    /// <summary>Writes an unsigned 40-bit integer in network byte order.</summary>
    /// <param name="destination">The five-byte destination field.</param>
    /// <param name="value">The value to encode.</param>
    private static void WriteUInt40(Span<byte> destination, ulong value)
    {
        for (var index = 4; index >= 0; index--)
        {
            destination[index] = (byte)value;
            value >>= 8;
        }
    }
}
