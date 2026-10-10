using System.Buffers.Binary;
using Psarc2Tab.Formats.PSARC;

namespace Psarc2Tab.PSARC.Tests;

public sealed class PsarcHeaderReaderTests
{
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

    [Fact]
    public void Read_TruncatedHeader_ThrowsInvalidDataException()
    {
        using var stream = new MemoryStream(new byte[PsarcHeaderReader.HeaderSize - 1]);

        var exception = Assert.Throws<InvalidDataException>(() => PsarcHeaderReader.Read(stream));

        Assert.Contains("truncated", exception.Message);
    }

    [Fact]
    public void Read_InvalidSignature_ThrowsInvalidDataException()
    {
        var archive = CreateArchive(0x468);
        archive[0] = (byte)'X';
        using var stream = new MemoryStream(archive);

        var exception = Assert.Throws<InvalidDataException>(() => PsarcHeaderReader.Read(stream));

        Assert.Contains("signature", exception.Message);
    }

    [Fact]
    public void Read_UnsupportedVersion_ThrowsInvalidDataException()
    {
        var archive = CreateArchive(0x468);
        WriteUInt32(archive, 4, 0x0001_0005);
        using var stream = new MemoryStream(archive);

        var exception = Assert.Throws<InvalidDataException>(() => PsarcHeaderReader.Read(stream));

        Assert.Contains("version 1.5", exception.Message);
    }

    [Fact]
    public void Read_UnsupportedCompression_ThrowsInvalidDataException()
    {
        var archive = CreateArchive(0x468);
        "lzma"u8.CopyTo(archive.AsSpan(8, 4));
        using var stream = new MemoryStream(archive);

        var exception = Assert.Throws<InvalidDataException>(() => PsarcHeaderReader.Read(stream));

        Assert.Contains("compression method 'lzma'", exception.Message);
    }

    [Fact]
    public void Read_UnsupportedTocEntrySize_ThrowsInvalidDataException()
    {
        var archive = CreateArchive(0x468);
        WriteUInt32(archive, 16, 31);
        using var stream = new MemoryStream(archive);

        var exception = Assert.Throws<InvalidDataException>(() => PsarcHeaderReader.Read(stream));

        Assert.Contains("TOC entry size 31", exception.Message);
    }

    [Fact]
    public void Read_ZeroFileCount_ThrowsInvalidDataException()
    {
        var archive = CreateArchive(0x468);
        WriteUInt32(archive, 20, 0);
        using var stream = new MemoryStream(archive);

        var exception = Assert.Throws<InvalidDataException>(() => PsarcHeaderReader.Read(stream));

        Assert.Contains("file count", exception.Message);
    }

    [Fact]
    public void Read_UnsupportedBlockSize_ThrowsInvalidDataException()
    {
        var archive = CreateArchive(0x468);
        WriteUInt32(archive, 24, 32_768);
        using var stream = new MemoryStream(archive);

        var exception = Assert.Throws<InvalidDataException>(() => PsarcHeaderReader.Read(stream));

        Assert.Contains("block size 32768", exception.Message);
    }

    [Fact]
    public void Read_UnsupportedArchiveFlags_ThrowsInvalidDataException()
    {
        var archive = CreateArchive(0x468);
        WriteUInt32(archive, 28, 0);
        using var stream = new MemoryStream(archive);

        var exception = Assert.Throws<InvalidDataException>(() => PsarcHeaderReader.Read(stream));

        Assert.Contains("archive flags 0x0", exception.Message);
    }

    [Fact]
    public void Read_TocSmallerThanEntryTable_ThrowsInvalidDataException()
    {
        var archive = CreateArchive(60);
        using var stream = new MemoryStream(archive);

        var exception = Assert.Throws<InvalidDataException>(() => PsarcHeaderReader.Read(stream));

        Assert.Contains("at least 872 bytes", exception.Message);
    }

    [Fact]
    public void Read_TocBeyondEndOfArchive_ThrowsInvalidDataException()
    {
        var headerOnly = CreateArchive(0x468)[..PsarcHeaderReader.HeaderSize];
        using var stream = new MemoryStream(headerOnly);

        var exception = Assert.Throws<InvalidDataException>(() => PsarcHeaderReader.Read(stream));

        Assert.Contains("archive contains only 32 bytes", exception.Message);
    }

    [Fact]
    public void Read_NonSeekableStream_ThrowsArgumentException()
    {
        using var inner = new MemoryStream(CreateArchive(0x468));
        using var stream = new NonSeekableStream(inner);

        Assert.Throws<ArgumentException>(() => PsarcHeaderReader.Read(stream));
    }

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

    private static void WriteUInt32(byte[] destination, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(destination.AsSpan(offset, sizeof(uint)), value);

    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => inner.Read(buffer);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

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
