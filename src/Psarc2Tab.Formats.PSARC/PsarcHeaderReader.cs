using System.Buffers.Binary;
using System.Text;

namespace Psarc2Tab.Formats.PSARC;

/// <summary>Reads and validates supported PSARC archive headers.</summary>
public static class PsarcHeaderReader
{
    /// <summary>The fixed PSARC header length in bytes.</summary>
    public const int HeaderSize = 32;

    /// <summary>The supported PSARC version encoded as two big-endian 16-bit values.</summary>
    private const uint SupportedVersion = 0x0001_0004;

    /// <summary>The supported TOC entry size in bytes.</summary>
    private const uint SupportedTocEntrySize = 30;

    /// <summary>The supported uncompressed block size in bytes.</summary>
    private const uint SupportedBlockSize = 65_536;

    /// <summary>The archive flag value observed in the supplied PC and Mac examples.</summary>
    private const uint SupportedArchiveFlags = 0x4;

    /// <summary>The four-byte PSARC signature.</summary>
    private static ReadOnlySpan<byte> Signature => "PSAR"u8;

    /// <summary>The supported four-byte compression method identifier.</summary>
    private static ReadOnlySpan<byte> SupportedCompressionMethod => "zlib"u8;

    /// <summary>
    /// Reads and validates the common 32-byte header used by the supplied PC
    /// and Mac Rocksmith archives. The stream must be readable and seekable.
    /// The stream position advances by <see cref="HeaderSize"/> bytes.
    /// </summary>
    /// <exception cref="InvalidDataException">The header is truncated or contains unsupported or inconsistent metadata.</exception>
    /// <exception cref="ArgumentException">The stream is not readable or seekable.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    public static PsarcHeader Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException("A PSARC stream must be readable and seekable.", nameof(stream));
        }

        var archiveStart = stream.Position;
        var remainingLength = stream.Length - archiveStart;
        if (remainingLength < HeaderSize)
        {
            throw new InvalidDataException("The PSARC header is truncated; expected 32 bytes.");
        }

        Span<byte> header = stackalloc byte[HeaderSize];
        try
        {
            stream.ReadExactly(header);
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException("The PSARC header is truncated; expected 32 bytes.", exception);
        }

        if (!header[..4].SequenceEqual(Signature))
        {
            throw new InvalidDataException("Invalid PSARC signature; expected 'PSAR'.");
        }

        var versionValue = BinaryPrimitives.ReadUInt32BigEndian(header[4..8]);
        if (versionValue != SupportedVersion)
        {
            var version = FormatVersion(versionValue);
            throw new InvalidDataException($"Unsupported PSARC version {version}; supported version is 1.4.");
        }

        if (!header[8..12].SequenceEqual(SupportedCompressionMethod))
        {
            var compression = Encoding.ASCII.GetString(header[8..12]);
            throw new InvalidDataException($"Unsupported PSARC compression method '{compression}'; supported method is 'zlib'.");
        }

        var tocSize = BinaryPrimitives.ReadUInt32BigEndian(header[12..16]);
        var tocEntrySize = BinaryPrimitives.ReadUInt32BigEndian(header[16..20]);
        var fileCount = BinaryPrimitives.ReadUInt32BigEndian(header[20..24]);
        var blockSize = BinaryPrimitives.ReadUInt32BigEndian(header[24..28]);
        var archiveFlags = BinaryPrimitives.ReadUInt32BigEndian(header[28..32]);

        if (tocEntrySize != SupportedTocEntrySize)
        {
            throw new InvalidDataException($"Unsupported PSARC TOC entry size {tocEntrySize}; supported size is {SupportedTocEntrySize} bytes.");
        }

        if (fileCount == 0)
        {
            throw new InvalidDataException("Invalid PSARC file count; the archive must contain at least one TOC entry.");
        }

        if (blockSize != SupportedBlockSize)
        {
            throw new InvalidDataException($"Unsupported PSARC block size {blockSize}; supported size is {SupportedBlockSize} bytes.");
        }

        if (archiveFlags != SupportedArchiveFlags)
        {
            throw new InvalidDataException($"Unsupported PSARC archive flags 0x{archiveFlags:X}; supported value is 0x{SupportedArchiveFlags:X}.");
        }

        var minimumTocSize = (ulong)HeaderSize + (ulong)fileCount * tocEntrySize;
        if (tocSize < minimumTocSize)
        {
            throw new InvalidDataException(
                $"Invalid PSARC TOC size {tocSize}; at least {minimumTocSize} bytes are required for {fileCount} entries.");
        }

        if (tocSize > (ulong)remainingLength)
        {
            throw new InvalidDataException(
                $"Invalid PSARC TOC size {tocSize}; the archive contains only {remainingLength} bytes from its start.");
        }

        return new PsarcHeader(
            new PsarcVersion((ushort)(versionValue >> 16), (ushort)versionValue),
            "zlib",
            tocSize,
            tocEntrySize,
            fileCount,
            blockSize,
            archiveFlags);
    }

    /// <summary>Formats a packed PSARC version as dotted major and minor values.</summary>
    /// <param name="value">The version encoded as two 16-bit values.</param>
    /// <returns>The version in <c>major.minor</c> form.</returns>
    private static string FormatVersion(uint value) => $"{value >> 16}.{value & 0xffff}";
}
