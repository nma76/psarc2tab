using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Psarc2Tab.Formats.PSARC;

/// <summary>Reads and validates the PSARC file records and block-size table.</summary>
public static class PsarcIndexReader
{
    /// <summary>The byte width of a block-size value for 64 KiB PSARC blocks.</summary>
    public const int BlockSizeFieldWidth = 2;

    /// <summary>The largest TOC payload accepted to bound memory use on untrusted archives.</summary>
    public const int MaximumTocPayloadSize = 64 * 1024 * 1024;

    /// <summary>The fixed 30-byte file-record layout used by the supported archive version.</summary>
    private const int FileRecordSize = 30;

    /// <summary>The 16-byte Rocksmith key used to encrypt supported archive TOCs.</summary>
    private static readonly byte[] TocEncryptionKey =
    [
        0xC5, 0x3D, 0xB2, 0x38, 0x70, 0xA1, 0xA2, 0xF7,
        0x1C, 0xAE, 0x64, 0x06, 0x1F, 0xDD, 0x0E, 0x11,
        0x57, 0x30, 0x9D, 0xC8, 0x52, 0x04, 0xD4, 0xC5,
        0xBF, 0xDF, 0x25, 0x09, 0x0D, 0xF2, 0x57, 0x2C
    ];

    /// <summary>Reads and validates the TOC payload following a parsed PSARC header.</summary>
    /// <param name="stream">The readable, seekable archive stream positioned after its header.</param>
    /// <param name="header">The validated PSARC header for this archive.</param>
    /// <returns>The archive entries and block sizes in TOC order.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentException">The stream is not readable or seekable, or is not positioned after its header.</exception>
    /// <exception cref="InvalidDataException">The TOC is malformed, unsupported, truncated, or exceeds the configured size limit.</exception>
    public static PsarcArchiveIndex Read(Stream stream, PsarcHeader header)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(header);

        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException("A PSARC stream must be readable and seekable.", nameof(stream));
        }

        var archiveStart = stream.Position - PsarcHeaderReader.HeaderSize;
        if (archiveStart < 0)
        {
            throw new ArgumentException("The stream must be positioned immediately after the PSARC header.", nameof(stream));
        }

        var tocPayloadSize = (ulong)header.TocSize - PsarcHeaderReader.HeaderSize;
        if (tocPayloadSize > MaximumTocPayloadSize)
        {
            throw new InvalidDataException($"PSARC TOC payload size {tocPayloadSize} exceeds the supported limit of {MaximumTocPayloadSize} bytes.");
        }

        var expectedTocEnd = checked(archiveStart + header.TocSize);
        if (expectedTocEnd > stream.Length)
        {
            throw new InvalidDataException("The PSARC TOC extends beyond the end of the archive.");
        }

        var payload = new byte[(int)tocPayloadSize];
        try
        {
            stream.ReadExactly(payload);
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException("The PSARC TOC is truncated.", exception);
        }

        if (header.ArchiveFlags == 0x4)
        {
            DecryptToc(payload);
        }

        var entriesLength = (ulong)header.FileCount * FileRecordSize;
        if (entriesLength > (ulong)payload.Length)
        {
            throw new InvalidDataException("The PSARC TOC is too small for its declared file records.");
        }

        var blockTableLength = (ulong)payload.Length - entriesLength;
        if (blockTableLength % BlockSizeFieldWidth != 0)
        {
            throw new InvalidDataException("The PSARC block-size table has an incomplete value.");
        }

        var blockCount = checked((int)(blockTableLength / BlockSizeFieldWidth));
        var blockSizes = new ushort[blockCount];
        var blockTableOffset = checked((int)entriesLength);
        for (var index = 0; index < blockCount; index++)
        {
            var offset = blockTableOffset + index * BlockSizeFieldWidth;
            blockSizes[index] = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(offset, BlockSizeFieldWidth));
            if (blockSizes[index] > header.BlockSize)
            {
                throw new InvalidDataException($"PSARC block {index} has unsupported compressed size {blockSizes[index]}.");
            }
        }

        var entries = new PsarcIndexEntry[checked((int)header.FileCount)];
        for (var index = 0; index < entries.Length; index++)
        {
            var recordOffset = index * FileRecordSize;
            var record = payload.AsSpan(recordOffset, FileRecordSize);
            var firstBlockIndex = BinaryPrimitives.ReadUInt32BigEndian(record[16..20]);
            var uncompressedLength = ReadUInt40BigEndian(record[20..25]);
            var dataOffset = ReadUInt40BigEndian(record[25..30]);

            ValidateEntry(index, firstBlockIndex, uncompressedLength, dataOffset, blockCount, header, stream.Length - archiveStart);
            entries[index] = new PsarcIndexEntry(record[..16], firstBlockIndex, uncompressedLength, dataOffset);
        }

        return new PsarcArchiveIndex(entries, blockSizes);
    }

    /// <summary>Reads a five-byte unsigned integer in network byte order.</summary>
    /// <param name="value">The five-byte field.</param>
    /// <returns>The decoded unsigned integer.</returns>
    private static ulong ReadUInt40BigEndian(ReadOnlySpan<byte> value) =>
        ((ulong)value[0] << 32) |
        ((ulong)value[1] << 24) |
        ((ulong)value[2] << 16) |
        ((ulong)value[3] << 8) |
        value[4];

    /// <summary>Checks that an entry references valid blocks and data inside the archive.</summary>
    /// <param name="entryIndex">The zero-based TOC entry position.</param>
    /// <param name="firstBlockIndex">The first block index declared by the entry.</param>
    /// <param name="uncompressedLength">The declared uncompressed file length.</param>
    /// <param name="dataOffset">The declared archive-relative data offset.</param>
    /// <param name="blockCount">The number of available block-size values.</param>
    /// <param name="header">The parsed archive header.</param>
    /// <param name="archiveLength">The archive length from its start.</param>
    /// <exception cref="InvalidDataException">The entry references invalid block indices or offsets.</exception>
    private static void ValidateEntry(
        int entryIndex,
        uint firstBlockIndex,
        ulong uncompressedLength,
        ulong dataOffset,
        int blockCount,
        PsarcHeader header,
        long archiveLength)
    {
        var requiredBlocks = uncompressedLength == 0
            ? 0UL
            : (uncompressedLength + header.BlockSize - 1) / header.BlockSize;
        if ((ulong)firstBlockIndex + requiredBlocks > (ulong)blockCount)
        {
            throw new InvalidDataException($"PSARC entry {entryIndex} references blocks beyond the block-size table.");
        }

        if (dataOffset < header.TocSize || dataOffset > (ulong)archiveLength)
        {
            throw new InvalidDataException($"PSARC entry {entryIndex} has data offset {dataOffset} outside the archive data region.");
        }
    }

    /// <summary>Decrypts the Rocksmith TOC with AES-256 CFB and a zero initialization vector.</summary>
    /// <param name="payload">The encrypted TOC bytes, replaced in place with plaintext.</param>
    /// <exception cref="CryptographicException">The platform AES implementation cannot initialize the cipher.</exception>
    private static void DecryptToc(Span<byte> payload)
    {
        using var aes = Aes.Create();
        aes.Key = TocEncryptionKey;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;

        Span<byte> feedback = stackalloc byte[16];
        Span<byte> keyStream = stackalloc byte[16];
        Span<byte> cipherBlock = stackalloc byte[16];

        for (var offset = 0; offset < payload.Length; offset += feedback.Length)
        {
            var blockLength = Math.Min(feedback.Length, payload.Length - offset);
            var block = payload.Slice(offset, blockLength);
            block.CopyTo(cipherBlock);
            aes.EncryptEcb(feedback, keyStream, PaddingMode.None);

            for (var index = 0; index < blockLength; index++)
            {
                block[index] ^= keyStream[index];
            }

            if (blockLength == feedback.Length)
            {
                cipherBlock.CopyTo(feedback);
            }
        }
    }
}
