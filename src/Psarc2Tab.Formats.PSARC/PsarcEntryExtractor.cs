using System.IO.Compression;

namespace Psarc2Tab.Formats.PSARC;

/// <summary>Extracts individual PSARC entries into a caller-provided stream.</summary>
public static class PsarcEntryExtractor
{
    /// <summary>Extracts one entry while limiting working memory to a single data block.</summary>
    /// <param name="archiveStream">The readable and seekable PSARC stream.</param>
    /// <param name="header">The validated archive header.</param>
    /// <param name="index">The parsed index created from this archive stream.</param>
    /// <param name="entryIndex">The zero-based entry position in the index.</param>
    /// <param name="destination">The writable stream receiving uncompressed file bytes.</param>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentException">A stream does not have the required capabilities or is the archive stream itself.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="entryIndex"/> is outside the index.</exception>
    /// <exception cref="InvalidDataException">An entry's block data is malformed, truncated, or inconsistent with its declared length.</exception>
    /// <remarks>Bytes already written to <paramref name="destination"/> remain there if a later block fails.</remarks>
    public static void Extract(Stream archiveStream, PsarcHeader header, PsarcArchiveIndex index, int entryIndex, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(archiveStream);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(destination);

        if (!archiveStream.CanRead || !archiveStream.CanSeek)
        {
            throw new ArgumentException("A PSARC archive stream must be readable and seekable.", nameof(archiveStream));
        }

        if (!destination.CanWrite)
        {
            throw new ArgumentException("The extraction destination must be writable.", nameof(destination));
        }

        if (ReferenceEquals(archiveStream, destination))
        {
            throw new ArgumentException("The archive stream cannot also be the extraction destination.", nameof(destination));
        }

        if ((uint)entryIndex >= (uint)index.Entries.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(entryIndex), entryIndex, "The entry index is outside the archive index.");
        }

        var entry = index.Entries[entryIndex];
        var requiredBlockCount = entry.UncompressedLength == 0
            ? 0UL
            : (entry.UncompressedLength + header.BlockSize - 1) / header.BlockSize;
        if ((ulong)entry.FirstBlockIndex + requiredBlockCount > (ulong)index.BlockSizes.Count)
        {
            throw new InvalidDataException($"PSARC entry {entryIndex} references blocks beyond the block-size table.");
        }

        var relativeOffset = entry.DataOffset;
        var remainingLength = entry.UncompressedLength;
        for (ulong blockNumber = 0; blockNumber < requiredBlockCount; blockNumber++)
        {
            var blockTableIndex = checked((int)(entry.FirstBlockIndex + blockNumber));
            var uncompressedBlockLength = (int)Math.Min((ulong)header.BlockSize, remainingLength);
            var storedBlockLength = index.BlockSizes[blockTableIndex];
            var absoluteOffset = GetAbsoluteOffset(index.ArchiveStart, relativeOffset, storedBlockLength == 0
                ? (ulong)uncompressedBlockLength
                : storedBlockLength, archiveStream.Length, entryIndex, blockNumber);

            archiveStream.Position = absoluteOffset;
            if (storedBlockLength == 0)
            {
                var rawBlock = new byte[uncompressedBlockLength];
                ReadBlock(archiveStream, rawBlock, entryIndex, blockNumber);
                destination.Write(rawBlock);
                relativeOffset += (ulong)rawBlock.Length;
            }
            else
            {
                var compressedBlock = new byte[storedBlockLength];
                ReadBlock(archiveStream, compressedBlock, entryIndex, blockNumber);
                var uncompressedBlock = DecompressBlock(compressedBlock, uncompressedBlockLength, entryIndex, blockNumber);
                destination.Write(uncompressedBlock);
                relativeOffset += storedBlockLength;
            }

            remainingLength -= (ulong)uncompressedBlockLength;
        }

        if (remainingLength != 0)
        {
            throw new InvalidDataException($"PSARC entry {entryIndex} ended before its declared uncompressed length.");
        }
    }

    /// <summary>Calculates and validates the absolute stream offset of a stored block.</summary>
    /// <param name="archiveStart">The stream offset where the archive begins.</param>
    /// <param name="relativeOffset">The block offset relative to the archive.</param>
    /// <param name="storedLength">The number of bytes stored for the block.</param>
    /// <param name="streamLength">The archive stream length.</param>
    /// <param name="entryIndex">The owning entry index.</param>
    /// <param name="blockNumber">The block position within the entry.</param>
    /// <returns>The absolute stream offset for the block.</returns>
    /// <exception cref="InvalidDataException">The block extends beyond the archive data.</exception>
    private static long GetAbsoluteOffset(long archiveStart, ulong relativeOffset, ulong storedLength, long streamLength, int entryIndex, ulong blockNumber)
    {
        var absoluteOffset = (ulong)archiveStart + relativeOffset;
        if (absoluteOffset > (ulong)streamLength || storedLength > (ulong)streamLength - absoluteOffset)
        {
            throw new InvalidDataException($"PSARC entry {entryIndex} block {blockNumber} extends beyond the end of the archive.");
        }

        return checked((long)absoluteOffset);
    }

    /// <summary>Reads one complete stored block from the archive.</summary>
    /// <param name="archiveStream">The archive stream positioned at the block.</param>
    /// <param name="block">The buffer receiving the block.</param>
    /// <param name="entryIndex">The owning entry index.</param>
    /// <param name="blockNumber">The block position within the entry.</param>
    /// <exception cref="InvalidDataException">The archive ends before the block is complete.</exception>
    private static void ReadBlock(Stream archiveStream, Span<byte> block, int entryIndex, ulong blockNumber)
    {
        try
        {
            archiveStream.ReadExactly(block);
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException($"PSARC entry {entryIndex} block {blockNumber} is truncated.", exception);
        }
    }

    /// <summary>Inflates one zlib block and checks its exact expected output size.</summary>
    /// <param name="compressedBlock">The complete compressed data block.</param>
    /// <param name="expectedLength">The expected uncompressed block length.</param>
    /// <param name="entryIndex">The owning entry index.</param>
    /// <param name="blockNumber">The block position within the entry.</param>
    /// <returns>The validated uncompressed block.</returns>
    /// <exception cref="InvalidDataException">The compressed data is invalid or produces an unexpected amount of data.</exception>
    private static byte[] DecompressBlock(byte[] compressedBlock, int expectedLength, int entryIndex, ulong blockNumber)
    {
        try
        {
            using var input = new MemoryStream(compressedBlock, writable: false);
            using var inflater = new ZLibStream(input, CompressionMode.Decompress);
            var output = new byte[expectedLength];
            var bytesRead = 0;
            while (bytesRead < output.Length)
            {
                var count = inflater.Read(output, bytesRead, output.Length - bytesRead);
                if (count == 0)
                {
                    throw new InvalidDataException("The compressed block ended before its expected length.");
                }

                bytesRead += count;
            }

            if (inflater.ReadByte() != -1)
            {
                throw new InvalidDataException("The compressed block expands beyond its expected length.");
            }

            return output;
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidDataException($"PSARC entry {entryIndex} block {blockNumber} contains invalid zlib data: {exception.Message}", exception);
        }
    }
}
