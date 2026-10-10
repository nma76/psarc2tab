using System.Buffers.Binary;
using System.Security.Cryptography;
using Psarc2Tab.Formats.PSARC;

namespace Psarc2Tab.PSARC.Tests;

/// <summary>Verifies PSARC TOC parsing, validation, and encrypted TOC handling.</summary>
public sealed class PsarcIndexReaderTests
{
    /// <summary>Reads entry records and block sizes from an unencrypted TOC.</summary>
    [Fact]
    public void Read_ValidUnencryptedToc_ReturnsEntriesAndBlockSizes()
    {
        var archive = CreateArchive(encrypted: false);
        using var stream = new MemoryStream(archive);
        var header = PsarcHeaderReader.Read(stream);

        var index = PsarcIndexReader.Read(stream, header);

        Assert.Equal(2, index.Entries.Count);
        Assert.Equal(0u, index.Entries[0].FirstBlockIndex);
        Assert.Equal(65_536UL, index.Entries[0].UncompressedLength);
        Assert.Equal(96UL, index.Entries[0].DataOffset);
        Assert.Equal(1u, index.Entries[1].FirstBlockIndex);
        Assert.Equal(1UL, index.Entries[1].UncompressedLength);
        Assert.Equal(new ushort[] { 100, 5 }, index.BlockSizes);
        Assert.Equal(0x11, index.Entries[0].PathDigest.Span[0]);
    }

    /// <summary>Decrypts encrypted TOC bytes before parsing their entries and block sizes.</summary>
    [Fact]
    public void Read_ValidEncryptedToc_ReturnsEntriesAndBlockSizes()
    {
        var archive = CreateArchive(encrypted: true);
        using var stream = new MemoryStream(archive);
        var header = PsarcHeaderReader.Read(stream);

        var index = PsarcIndexReader.Read(stream, header);

        Assert.Equal(2, index.Entries.Count);
        Assert.Equal(65_536UL, index.Entries[0].UncompressedLength);
        Assert.Equal(new ushort[] { 100, 5 }, index.BlockSizes);
    }

    /// <summary>Rejects a file record whose required blocks exceed the block-size table.</summary>
    [Fact]
    public void Read_BlockIndexBeyondTable_ThrowsInvalidDataException()
    {
        var archive = CreateArchive(encrypted: false);
        WriteUInt32(archive, PsarcHeaderReader.HeaderSize + 16, 2);
        using var stream = new MemoryStream(archive);
        var header = PsarcHeaderReader.Read(stream);

        var exception = Assert.Throws<InvalidDataException>(() => PsarcIndexReader.Read(stream, header));

        Assert.Contains("beyond the block-size table", exception.Message);
    }

    /// <summary>Rejects a file record that points back into the TOC.</summary>
    [Fact]
    public void Read_DataOffsetBeforeDataRegion_ThrowsInvalidDataException()
    {
        var archive = CreateArchive(encrypted: false);
        WriteUInt40(archive, PsarcHeaderReader.HeaderSize + 25, 95);
        using var stream = new MemoryStream(archive);
        var header = PsarcHeaderReader.Read(stream);

        var exception = Assert.Throws<InvalidDataException>(() => PsarcIndexReader.Read(stream, header));

        Assert.Contains("outside the archive data region", exception.Message);
    }

    /// <summary>Rejects a TOC whose block-size table ends in a partial value.</summary>
    [Fact]
    public void Read_IncompleteBlockSizeValue_ThrowsInvalidDataException()
    {
        var archive = CreateArchive(encrypted: false);
        Array.Resize(ref archive, archive.Length - 1);
        WriteUInt32(archive, 12, 95);
        using var stream = new MemoryStream(archive);
        var header = PsarcHeaderReader.Read(stream);

        var exception = Assert.Throws<InvalidDataException>(() => PsarcIndexReader.Read(stream, header));

        Assert.Contains("incomplete value", exception.Message);
    }

    /// <summary>Rejects a TOC reader call unless the stream is positioned after its header.</summary>
    [Fact]
    public void Read_WrongStreamPosition_ThrowsArgumentException()
    {
        var archive = CreateArchive(encrypted: false);
        using var stream = new MemoryStream(archive);
        var header = PsarcHeaderReader.Read(stream);
        stream.Position = 0;

        Assert.Throws<ArgumentException>(() => PsarcIndexReader.Read(stream, header));
    }

    /// <summary>Creates a synthetic two-entry archive with optional TOC encryption.</summary>
    /// <param name="encrypted">Whether to encrypt the TOC payload.</param>
    /// <returns>A complete synthetic PSARC archive.</returns>
    private static byte[] CreateArchive(bool encrypted)
    {
        const int tocSize = 96;
        var archive = new byte[tocSize + 9];
        "PSAR"u8.CopyTo(archive);
        WriteUInt32(archive, 4, 0x0001_0004);
        "zlib"u8.CopyTo(archive.AsSpan(8, 4));
        WriteUInt32(archive, 12, tocSize);
        WriteUInt32(archive, 16, 30);
        WriteUInt32(archive, 20, 2);
        WriteUInt32(archive, 24, 65_536);
        WriteUInt32(archive, 28, encrypted ? 4u : 0u);

        var toc = archive.AsSpan(PsarcHeaderReader.HeaderSize, tocSize - PsarcHeaderReader.HeaderSize);
        toc[0] = 0x11;
        WriteUInt32(toc, 16, 0);
        WriteUInt40(toc, 20, 65_536);
        WriteUInt40(toc, 25, tocSize);

        toc[30] = 0x22;
        WriteUInt32(toc, 46, 1);
        WriteUInt40(toc, 50, 1);
        WriteUInt40(toc, 55, tocSize + 4);

        BinaryPrimitives.WriteUInt16BigEndian(toc[60..62], 100);
        BinaryPrimitives.WriteUInt16BigEndian(toc[62..64], 5);
        if (encrypted)
        {
            EncryptToc(toc);
        }

        return archive;
    }

    /// <summary>Encrypts a synthetic TOC using the archive's AES-256 CFB key and zero IV.</summary>
    /// <param name="payload">The TOC payload encrypted in place.</param>
    private static void EncryptToc(Span<byte> payload)
    {
        byte[] key =
        [
            0xC5, 0x3D, 0xB2, 0x38, 0x70, 0xA1, 0xA2, 0xF7,
            0x1C, 0xAE, 0x64, 0x06, 0x1F, 0xDD, 0x0E, 0x11,
            0x57, 0x30, 0x9D, 0xC8, 0x52, 0x04, 0xD4, 0xC5,
            0xBF, 0xDF, 0x25, 0x09, 0x0D, 0xF2, 0x57, 0x2C
        ];
        using var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;

        Span<byte> feedback = stackalloc byte[16];
        Span<byte> keyStream = stackalloc byte[16];
        Span<byte> plainBlock = stackalloc byte[16];
        for (var offset = 0; offset < payload.Length; offset += feedback.Length)
        {
            var blockLength = Math.Min(feedback.Length, payload.Length - offset);
            var block = payload.Slice(offset, blockLength);
            block.CopyTo(plainBlock);
            aes.EncryptEcb(feedback, keyStream, PaddingMode.None);
            for (var index = 0; index < blockLength; index++)
            {
                block[index] ^= keyStream[index];
            }

            if (blockLength == feedback.Length)
            {
                block.CopyTo(feedback);
            }
        }
    }

    /// <summary>Writes a 32-bit unsigned integer in network byte order.</summary>
    /// <param name="destination">The destination byte array.</param>
    /// <param name="offset">The first byte to write.</param>
    /// <param name="value">The value to encode.</param>
    private static void WriteUInt32(byte[] destination, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(destination.AsSpan(offset, sizeof(uint)), value);

    /// <summary>Writes a 32-bit unsigned integer in network byte order.</summary>
    /// <param name="destination">The destination span.</param>
    /// <param name="offset">The first byte to write.</param>
    /// <param name="value">The value to encode.</param>
    private static void WriteUInt32(Span<byte> destination, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(offset, sizeof(uint)), value);

    /// <summary>Writes an unsigned 40-bit integer in network byte order.</summary>
    /// <param name="destination">The destination byte array.</param>
    /// <param name="offset">The first byte to write.</param>
    /// <param name="value">The value to encode.</param>
    private static void WriteUInt40(byte[] destination, int offset, ulong value) =>
        WriteUInt40(destination.AsSpan(offset, 5), value);

    /// <summary>Writes an unsigned 40-bit integer in network byte order.</summary>
    /// <param name="destination">The destination span.</param>
    /// <param name="offset">The first byte to write.</param>
    /// <param name="value">The value to encode.</param>
    private static void WriteUInt40(Span<byte> destination, int offset, ulong value) =>
        WriteUInt40(destination.Slice(offset, 5), value);

    /// <summary>Writes an unsigned 40-bit integer in network byte order.</summary>
    /// <param name="destination">The five-byte destination.</param>
    /// <param name="value">The value to encode.</param>
    private static void WriteUInt40(Span<byte> destination, ulong value)
    {
        for (var index = destination.Length - 1; index >= 0; index--)
        {
            destination[index] = (byte)value;
            value >>= 8;
        }
    }
}
