using System.Buffers.Binary;
using System.IO.Compression;
using Psarc2Tab.Formats.PSARC;

namespace Psarc2Tab.PSARC.Tests;

/// <summary>Verifies safe extraction of raw and zlib-compressed PSARC entries.</summary>
public sealed class PsarcEntryExtractorTests
{
    /// <summary>Extracts a multi-block file whose blocks use zlib compression.</summary>
    [Fact]
    public void Extract_CompressedMultiBlockEntry_WritesOriginalBytes()
    {
        var content = Enumerable.Range(0, 150_000).Select(value => (byte)(value % 13)).ToArray();
        var archive = CreateArchive(content, compress: true);
        using var archiveStream = new MemoryStream(archive);
        var (header, index) = ReadIndex(archiveStream);
        using var output = new MemoryStream();

        PsarcEntryExtractor.Extract(archiveStream, header, index, 0, output);

        Assert.Equal(content, output.ToArray());
    }

    /// <summary>Copies an uncompressed block when its block-size value is zero.</summary>
    [Fact]
    public void Extract_RawEntry_WritesOriginalBytes()
    {
        var content = Enumerable.Range(0, 90_000).Select(value => (byte)(value % 251)).ToArray();
        var archive = CreateArchive(content, compress: false);
        using var archiveStream = new MemoryStream(archive);
        var (header, index) = ReadIndex(archiveStream);
        using var output = new MemoryStream();

        PsarcEntryExtractor.Extract(archiveStream, header, index, 0, output);

        Assert.Equal(content, output.ToArray());
    }

    /// <summary>Copies raw bytes when the block table records their stored length.</summary>
    [Fact]
    public void Extract_RawEntryWithNonzeroStoredSize_WritesOriginalBytes()
    {
        var content = "raw block"u8.ToArray();
        var archive = CreateArchive(content, compress: false);
        BinaryPrimitives.WriteUInt16BigEndian(archive.AsSpan(PsarcHeaderReader.HeaderSize + 30, 2), (ushort)content.Length);
        using var archiveStream = new MemoryStream(archive);
        var (header, index) = ReadIndex(archiveStream);
        using var output = new MemoryStream();

        PsarcEntryExtractor.Extract(archiveStream, header, index, 0, output);

        Assert.Equal(content, output.ToArray());
    }

    /// <summary>Returns an empty output for an entry whose declared length is zero.</summary>
    [Fact]
    public void Extract_EmptyEntry_WritesNoBytes()
    {
        var archive = CreateArchive([], compress: true);
        using var archiveStream = new MemoryStream(archive);
        var (header, index) = ReadIndex(archiveStream);
        using var output = new MemoryStream();

        PsarcEntryExtractor.Extract(archiveStream, header, index, 0, output);

        Assert.Empty(output.ToArray());
    }

    /// <summary>Rejects compressed data that cannot be decoded as a zlib stream.</summary>
    [Fact]
    public void Extract_InvalidCompressedBlock_ThrowsInvalidDataException()
    {
        var archive = CreateArchive(Enumerable.Repeat((byte)'x', 1_000).ToArray(), compress: true);
        var tocSize = ReadUInt32(archive, 12);
        archive[tocSize] = 0;
        using var archiveStream = new MemoryStream(archive);
        var (header, index) = ReadIndex(archiveStream);
        using var output = new MemoryStream();

        var exception = Assert.Throws<InvalidDataException>(() => PsarcEntryExtractor.Extract(archiveStream, header, index, 0, output));

        Assert.Contains("invalid zlib data", exception.Message);
    }

    /// <summary>Rejects a zlib block that expands beyond the entry's declared length.</summary>
    [Fact]
    public void Extract_DecompressedLengthExceedsEntryLength_ThrowsInvalidDataException()
    {
        var archive = CreateArchive(Enumerable.Repeat((byte)'x', 1_000).ToArray(), compress: true);
        WriteUInt40(archive, PsarcHeaderReader.HeaderSize + 20, 999);
        using var archiveStream = new MemoryStream(archive);
        var (header, index) = ReadIndex(archiveStream);
        using var output = new MemoryStream();

        var exception = Assert.Throws<InvalidDataException>(() => PsarcEntryExtractor.Extract(archiveStream, header, index, 0, output));

        Assert.Contains("expands beyond its expected length", exception.Message);
    }

    /// <summary>Rejects a stored block whose bytes extend past the archive end.</summary>
    [Fact]
    public void Extract_TruncatedBlock_ThrowsInvalidDataException()
    {
        var archive = CreateArchive(Enumerable.Repeat((byte)'x', 1_000).ToArray(), compress: true);
        Array.Resize(ref archive, archive.Length - 1);
        using var archiveStream = new MemoryStream(archive);
        var (header, index) = ReadIndex(archiveStream);
        using var output = new MemoryStream();

        var exception = Assert.Throws<InvalidDataException>(() => PsarcEntryExtractor.Extract(archiveStream, header, index, 0, output));

        Assert.Contains("extends beyond the end of the archive", exception.Message);
    }

    /// <summary>Rejects entry indexes that do not exist in the parsed TOC.</summary>
    [Fact]
    public void Extract_EntryIndexOutOfRange_ThrowsArgumentOutOfRangeException()
    {
        var archive = CreateArchive("x"u8.ToArray(), compress: false);
        using var archiveStream = new MemoryStream(archive);
        var (header, index) = ReadIndex(archiveStream);
        using var output = new MemoryStream();

        Assert.Throws<ArgumentOutOfRangeException>(() => PsarcEntryExtractor.Extract(archiveStream, header, index, 1, output));
    }

    /// <summary>Rejects extraction into the archive stream itself.</summary>
    [Fact]
    public void Extract_ArchiveUsedAsDestination_ThrowsArgumentException()
    {
        var archive = CreateArchive("x"u8.ToArray(), compress: false);
        using var archiveStream = new MemoryStream(archive);
        var (header, index) = ReadIndex(archiveStream);

        Assert.Throws<ArgumentException>(() => PsarcEntryExtractor.Extract(archiveStream, header, index, 0, archiveStream));
    }

    /// <summary>Reads the header and index from the provided archive stream.</summary>
    /// <param name="archiveStream">The stream positioned at the archive start.</param>
    /// <returns>The parsed archive header and index.</returns>
    private static (PsarcHeader Header, PsarcArchiveIndex Index) ReadIndex(MemoryStream archiveStream)
    {
        var header = PsarcHeaderReader.Read(archiveStream);
        var index = PsarcIndexReader.Read(archiveStream, header);
        return (header, index);
    }

    /// <summary>Builds a one-entry archive with independently encoded data blocks.</summary>
    /// <param name="content">The uncompressed entry contents.</param>
    /// <param name="compress">Whether to store each block as zlib data.</param>
    /// <returns>A synthetic PSARC archive containing the entry.</returns>
    private static byte[] CreateArchive(byte[] content, bool compress)
    {
        const int headerSize = PsarcHeaderReader.HeaderSize;
        const int entrySize = 30;
        const uint blockSize = 65_536;
        var blocks = new List<(byte[] Data, ushort EncodedSize)>();
        for (var offset = 0; offset < content.Length; offset += (int)blockSize)
        {
            var length = Math.Min((int)blockSize, content.Length - offset);
            var block = content.AsSpan(offset, length).ToArray();
            var compressed = compress ? Compress(block) : Array.Empty<byte>();
            blocks.Add(compress && compressed.Length < block.Length && compressed.Length <= ushort.MaxValue
                ? (compressed, (ushort)compressed.Length)
                : (block, (ushort)0));
        }

        var tocSize = checked(headerSize + entrySize + blocks.Count * 2);
        var archiveLength = tocSize + blocks.Sum(block => block.Data.Length);
        var archive = new byte[archiveLength];
        "PSAR"u8.CopyTo(archive);
        WriteUInt32(archive, 4, 0x0001_0004);
        "zlib"u8.CopyTo(archive.AsSpan(8, 4));
        WriteUInt32(archive, 12, (uint)tocSize);
        WriteUInt32(archive, 16, entrySize);
        WriteUInt32(archive, 20, 1);
        WriteUInt32(archive, 24, blockSize);
        WriteUInt32(archive, 28, 0);
        WriteUInt32(archive, headerSize + 16, 0);
        WriteUInt40(archive, headerSize + 20, (ulong)content.Length);
        WriteUInt40(archive, headerSize + 25, (ulong)tocSize);

        var blockTableOffset = headerSize + entrySize;
        var dataOffset = tocSize;
        for (var index = 0; index < blocks.Count; index++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(archive.AsSpan(blockTableOffset + index * 2, 2), blocks[index].EncodedSize);
            blocks[index].Data.CopyTo(archive, dataOffset);
            dataOffset += blocks[index].Data.Length;
        }

        return archive;
    }

    /// <summary>Compresses one data block using the PSARC zlib wrapper.</summary>
    /// <param name="content">The uncompressed block bytes.</param>
    /// <returns>The compressed zlib stream.</returns>
    private static byte[] Compress(byte[] content)
    {
        using var output = new MemoryStream();
        using (var compressor = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            compressor.Write(content);
        }

        return output.ToArray();
    }

    /// <summary>Reads a 32-bit unsigned integer in network byte order.</summary>
    /// <param name="source">The source bytes.</param>
    /// <param name="offset">The first byte to read.</param>
    /// <returns>The decoded value.</returns>
    private static uint ReadUInt32(byte[] source, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(source.AsSpan(offset, sizeof(uint)));

    /// <summary>Writes a 32-bit unsigned integer in network byte order.</summary>
    /// <param name="destination">The destination bytes.</param>
    /// <param name="offset">The first byte to write.</param>
    /// <param name="value">The value to encode.</param>
    private static void WriteUInt32(byte[] destination, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(destination.AsSpan(offset, sizeof(uint)), value);

    /// <summary>Writes an unsigned 40-bit integer in network byte order.</summary>
    /// <param name="destination">The destination bytes.</param>
    /// <param name="offset">The first byte to write.</param>
    /// <param name="value">The value to encode.</param>
    private static void WriteUInt40(byte[] destination, int offset, ulong value)
    {
        for (var index = 4; index >= 0; index--)
        {
            destination[offset + index] = (byte)value;
            value >>= 8;
        }
    }
}
