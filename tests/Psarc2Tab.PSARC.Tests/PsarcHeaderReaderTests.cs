using System.Buffers.Binary;
using Psarc2Tab.Formats.PSARC;

namespace Psarc2Tab.PSARC.Tests;

/// <summary>Verifies PSARC header parsing and rejection of malformed metadata.</summary>
public sealed class PsarcHeaderReaderTests
{
    /// <summary>Parses either observed TOC size and returns its header fields.</summary>
    /// <param name="tocSize">The total TOC size from a supplied sample.</param>
    [Theory]
    [InlineData(0x468)]
    [InlineData(0x46A)]
    public void Read_ValidRocksmithHeader_ReturnsFields(uint tocSize)
    {
        using var stream = new MemoryStream(CreateArchive(tocSize));

        var header = PsarcHeaderReader.Read(stream);

        Assert.Equal(new PsarcVersion(1, 4), header.Version);
        Assert.Equal("zlib", header.CompressionMethod);
        Assert.Equal(tocSize, header.TocSize);
        Assert.Equal(30u, header.TocEntrySize);
        Assert.Equal(28u, header.FileCount);
        Assert.Equal(65_536u, header.BlockSize);
        Assert.Equal(0x4u, header.ArchiveFlags);
        Assert.Equal(PsarcHeaderReader.HeaderSize, stream.Position);
    }

    /// <summary>Reads a PSARC archive that begins at the stream's current position.</summary>
    [Fact]
    public void Read_AtNonzeroStreamPosition_UsesRemainingArchiveLength()
    {
        var archive = CreateArchive(0x468);
        var bytes = new byte[archive.Length + 7];
        archive.CopyTo(bytes, 7);
        using var stream = new MemoryStream(bytes);
        stream.Position = 7;

        var header = PsarcHeaderReader.Read(stream);

        Assert.Equal(0x468u, header.TocSize);
        Assert.Equal(7 + PsarcHeaderReader.HeaderSize, stream.Position);
    }

    /// <summary>Rejects streams that end before the fixed-size header is complete.</summary>
    [Fact]
    public void Read_TruncatedHeader_ThrowsInvalidDataException()
    {
        using var stream = new MemoryStream(new byte[PsarcHeaderReader.HeaderSize - 1]);

        var exception = Assert.Throws<InvalidDataException>(() => PsarcHeaderReader.Read(stream));

        Assert.Contains("truncated", exception.Message);
    }

    /// <summary>Rejects data without the PSARC signature.</summary>
    [Fact]
    public void Read_InvalidSignature_ThrowsInvalidDataException()
    {
        var archive = CreateArchive(0x468);
        archive[0] = (byte)'X';
        using var stream = new MemoryStream(archive);

        var exception = Assert.Throws<InvalidDataException>(() => PsarcHeaderReader.Read(stream));

        Assert.Contains("signature", exception.Message);
    }

    /// <summary>Rejects versions outside the supported 1.4 format.</summary>
    [Fact]
    public void Read_UnsupportedVersion_ThrowsInvalidDataException()
    {
        var archive = CreateArchive(0x468);
        WriteUInt32(archive, 4, 0x0001_0005);
        using var stream = new MemoryStream(archive);

        var exception = Assert.Throws<InvalidDataException>(() => PsarcHeaderReader.Read(stream));

        Assert.Contains("version 1.5", exception.Message);
    }

    /// <summary>Rejects compression methods outside the supported zlib format.</summary>
    [Fact]
    public void Read_UnsupportedCompression_ThrowsInvalidDataException()
    {
        var archive = CreateArchive(0x468);
        "lzma"u8.CopyTo(archive.AsSpan(8, 4));
        using var stream = new MemoryStream(archive);

        var exception = Assert.Throws<InvalidDataException>(() => PsarcHeaderReader.Read(stream));

        Assert.Contains("compression method 'lzma'", exception.Message);
    }

    /// <summary>Rejects TOC entries that do not use the supported 30-byte layout.</summary>
    [Fact]
    public void Read_UnsupportedTocEntrySize_ThrowsInvalidDataException()
    {
        var archive = CreateArchive(0x468);
        WriteUInt32(archive, 16, 31);
        using var stream = new MemoryStream(archive);

        var exception = Assert.Throws<InvalidDataException>(() => PsarcHeaderReader.Read(stream));

        Assert.Contains("TOC entry size 31", exception.Message);
    }

    /// <summary>Rejects archives that declare no file entries.</summary>
    [Fact]
    public void Read_ZeroFileCount_ThrowsInvalidDataException()
    {
        var archive = CreateArchive(0x468);
        WriteUInt32(archive, 20, 0);
        using var stream = new MemoryStream(archive);

        var exception = Assert.Throws<InvalidDataException>(() => PsarcHeaderReader.Read(stream));

        Assert.Contains("file count", exception.Message);
    }

    /// <summary>Rejects block sizes outside the supported 64 KiB layout.</summary>
    [Fact]
    public void Read_UnsupportedBlockSize_ThrowsInvalidDataException()
    {
        var archive = CreateArchive(0x468);
        WriteUInt32(archive, 24, 32_768);
        using var stream = new MemoryStream(archive);

        var exception = Assert.Throws<InvalidDataException>(() => PsarcHeaderReader.Read(stream));

        Assert.Contains("block size 32768", exception.Message);
    }

    /// <summary>Rejects archive flags that were not observed in the supported samples.</summary>
    [Fact]
    public void Read_UnsupportedArchiveFlags_ThrowsInvalidDataException()
    {
        var archive = CreateArchive(0x468);
        WriteUInt32(archive, 28, 1);
        using var stream = new MemoryStream(archive);

        var exception = Assert.Throws<InvalidDataException>(() => PsarcHeaderReader.Read(stream));

        Assert.Contains("archive flags 0x1", exception.Message);
    }

    /// <summary>Rejects TOC sizes too small to contain the declared entry table.</summary>
    [Fact]
    public void Read_TocSmallerThanEntryTable_ThrowsInvalidDataException()
    {
        var archive = CreateArchive(60);
        using var stream = new MemoryStream(archive);

        var exception = Assert.Throws<InvalidDataException>(() => PsarcHeaderReader.Read(stream));

        Assert.Contains("at least 872 bytes", exception.Message);
    }

    /// <summary>Rejects a TOC region that extends beyond the available archive bytes.</summary>
    [Fact]
    public void Read_TocBeyondEndOfArchive_ThrowsInvalidDataException()
    {
        var headerOnly = CreateArchive(0x468)[..PsarcHeaderReader.HeaderSize];
        using var stream = new MemoryStream(headerOnly);

        var exception = Assert.Throws<InvalidDataException>(() => PsarcHeaderReader.Read(stream));

        Assert.Contains("archive contains only 32 bytes", exception.Message);
    }

    /// <summary>Requires a seekable stream so the declared TOC range can be validated.</summary>
    [Fact]
    public void Read_NonSeekableStream_ThrowsArgumentException()
    {
        using var inner = new MemoryStream(CreateArchive(0x468));
        using var stream = new NonSeekableStream(inner);

        Assert.Throws<ArgumentException>(() => PsarcHeaderReader.Read(stream));
    }

    /// <summary>Builds a synthetic archive with the supplied TOC size and sample metadata.</summary>
    /// <param name="tocSize">The total TOC region size.</param>
    /// <returns>A byte array containing a zero-filled TOC region and a valid sample header.</returns>
    private static byte[] CreateArchive(uint tocSize)
    {
        var archive = new byte[checked((int)tocSize)];
        "PSAR"u8.CopyTo(archive);
        WriteUInt32(archive, 4, 0x0001_0004);
        "zlib"u8.CopyTo(archive.AsSpan(8, 4));
        WriteUInt32(archive, 12, tocSize);
        WriteUInt32(archive, 16, 30);
        WriteUInt32(archive, 20, 28);
        WriteUInt32(archive, 24, 65_536);
        WriteUInt32(archive, 28, 0x4);
        return archive;
    }

    /// <summary>Writes a 32-bit unsigned integer in network byte order.</summary>
    /// <param name="destination">The buffer that receives the encoded value.</param>
    /// <param name="offset">The first destination byte to write.</param>
    /// <param name="value">The unsigned value to encode.</param>
    private static void WriteUInt32(byte[] destination, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(destination.AsSpan(offset, sizeof(uint)), value);

    /// <summary>Wraps a stream while disabling seek and length operations.</summary>
    /// <param name="inner">The stream that supplies read and write operations.</param>
    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        /// <inheritdoc />
        public override bool CanRead => inner.CanRead;

        /// <inheritdoc />
        public override bool CanSeek => false;

        /// <inheritdoc />
        public override bool CanWrite => inner.CanWrite;

        /// <inheritdoc />
        public override long Length => throw new NotSupportedException();

        /// <inheritdoc />
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override void Flush() => inner.Flush();

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        /// <inheritdoc />
        public override int Read(Span<byte> buffer) => inner.Read(buffer);

        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        /// <inheritdoc />
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
