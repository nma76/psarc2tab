using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Psarc2Tab.Formats.Rocksmith;

/// <summary>Decodes encrypted PC and Mac Rocksmith SNG containers.</summary>
public sealed class SngContainerReader
{
    /// <summary>The largest decoded chart payload accepted by this reader.</summary>
    public const int MaximumDecodedLength = 64 * 1024 * 1024;

    /// <summary>The largest complete SNG container accepted by this reader.</summary>
    private const long MaximumContainerLength = MaximumDecodedLength + 1024 * 1024;

    /// <summary>The fixed signature size stored at the end of an encrypted SNG container.</summary>
    private const int SignatureLength = 56;

    /// <summary>The PC SNG encryption key.</summary>
    private static readonly byte[] PcKey = Convert.FromHexString(
        "CB648DF3D12A16BF71701414E69619EC171CCA5D2A142E3E59DE7ADDA18A3A30");

    /// <summary>The Mac SNG encryption key.</summary>
    private static readonly byte[] MacKey = Convert.FromHexString(
        "9821330E34B91F70D0A48CBD625993126970CEA09192C0E6CDA676CC9838289D");

    /// <summary>Determines the SNG platform from its normalized Rocksmith archive path.</summary>
    /// <param name="archivePath">The manifest path of the SNG entry.</param>
    /// <returns>The platform variant encoded by the path.</returns>
    /// <exception cref="ArgumentException">The path is not a supported PC or Mac SNG path.</exception>
    public static SngPlatform GetPlatform(string archivePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        if (archivePath.StartsWith("songs/bin/generic/", StringComparison.OrdinalIgnoreCase) &&
            archivePath.EndsWith(".sng", StringComparison.OrdinalIgnoreCase))
        {
            return SngPlatform.Pc;
        }

        if (archivePath.StartsWith("songs/bin/macos/", StringComparison.OrdinalIgnoreCase) &&
            archivePath.EndsWith(".sng", StringComparison.OrdinalIgnoreCase))
        {
            return SngPlatform.Mac;
        }

        throw new ArgumentException("The SNG archive path does not identify a supported PC or Mac variant.", nameof(archivePath));
    }

    /// <summary>Decrypts and inflates one encrypted SNG stream.</summary>
    /// <param name="source">The readable encrypted SNG stream.</param>
    /// <param name="platform">The platform variant indicated by its archive path.</param>
    /// <returns>The bounded, uncompressed SNG chart bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    /// <exception cref="ArgumentException">The source stream is not readable or seekable.</exception>
    /// <exception cref="InvalidDataException">The header, encrypted payload, or zlib data is invalid.</exception>
    public byte[] Decode(Stream source, SngPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead || !source.CanSeek)
        {
            throw new ArgumentException("An SNG stream must be readable and seekable.", nameof(source));
        }

        var isMac = platform switch
        {
            SngPlatform.Pc => false,
            SngPlatform.Mac => true,
            _ => throw new ArgumentOutOfRangeException(nameof(platform))
        };
        var minimumLength = 8L + 16 + sizeof(uint) + 2 + SignatureLength;
        if (source.Length < minimumLength)
        {
            throw new InvalidDataException("The SNG container is truncated.");
        }

        if (source.Length > MaximumContainerLength)
        {
            throw new InvalidDataException("The SNG container exceeds the supported 65 MiB size limit.");
        }

        Span<byte> header = stackalloc byte[8];
        source.Position = 0;
        source.ReadExactly(header);
        var magic = isMac
            ? BinaryPrimitives.ReadUInt32BigEndian(header)
            : BinaryPrimitives.ReadUInt32LittleEndian(header);
        var platformMarker = isMac
            ? BinaryPrimitives.ReadUInt32BigEndian(header[4..])
            : BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
        if (magic != 0x4A || platformMarker != (isMac ? 1U : 3U))
        {
            throw new InvalidDataException("The SNG header does not match its declared PC or Mac platform.");
        }

        Span<byte> counter = stackalloc byte[16];
        source.ReadExactly(counter);
        var cipherLength = checked((int)(source.Length - source.Position - SignatureLength));
        if (cipherLength < sizeof(uint) + 2)
        {
            throw new InvalidDataException("The SNG encrypted payload is truncated.");
        }

        var ciphertext = new byte[cipherLength];
        source.ReadExactly(ciphertext);
        var plaintext = TransformCounter(ciphertext, isMac ? MacKey : PcKey, counter);
        var decodedLength = isMac
            ? BinaryPrimitives.ReadUInt32BigEndian(plaintext)
            : BinaryPrimitives.ReadUInt32LittleEndian(plaintext);
        if (decodedLength is 0 or > MaximumDecodedLength)
        {
            throw new InvalidDataException($"The SNG decoded length {decodedLength} exceeds the supported size limit.");
        }

        try
        {
            using var compressed = new MemoryStream(plaintext, sizeof(uint), plaintext.Length - sizeof(uint), writable: false);
            using var inflater = new ZLibStream(compressed, CompressionMode.Decompress);
            var output = new byte[checked((int)decodedLength)];
            inflater.ReadExactly(output);
            if (inflater.ReadByte() != -1)
            {
                throw new InvalidDataException("The SNG zlib payload expands beyond its declared decoded length.");
            }

            return output;
        }
        catch (InvalidDataException exception) when (!exception.Message.Contains("declared decoded length", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"The SNG zlib payload is invalid or does not match its declared length: {exception.Message}", exception);
        }
        catch (IOException exception)
        {
            throw new InvalidDataException($"The SNG zlib payload is invalid: {exception.Message}", exception);
        }
    }

    /// <summary>Applies AES-CTR using the full 128-bit big-endian counter.</summary>
    /// <param name="input">The encrypted or decrypted bytes.</param>
    /// <param name="key">The 256-bit platform key.</param>
    /// <param name="initialCounter">The 16-byte counter from the SNG header.</param>
    /// <returns>The transformed bytes.</returns>
    private static byte[] TransformCounter(ReadOnlySpan<byte> input, byte[] key, ReadOnlySpan<byte> initialCounter)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        using var encryptor = aes.CreateEncryptor(key, new byte[16]);
        var counter = initialCounter.ToArray();
        var counterBlock = new byte[16];
        var result = new byte[input.Length];
        var offset = 0;
        while (offset < input.Length)
        {
            _ = encryptor.TransformBlock(counter, 0, counter.Length, counterBlock, 0);
            IncrementCounter(counter);
            var blockLength = Math.Min(counterBlock.Length, input.Length - offset);
            for (var index = 0; index < blockLength; index++)
            {
                result[offset + index] = (byte)(input[offset + index] ^ counterBlock[index]);
            }

            offset += blockLength;
        }

        return result;
    }

    /// <summary>Increments a counter represented as a big-endian byte array.</summary>
    /// <param name="counter">The counter updated in place.</param>
    private static void IncrementCounter(Span<byte> counter)
    {
        for (var index = counter.Length - 1; index >= 0; index--)
        {
            counter[index]++;
            if (counter[index] != 0)
            {
                return;
            }
        }
    }
}

/// <summary>Identifies the SNG key and byte order used by an archive entry.</summary>
public enum SngPlatform
{
    /// <summary>The PC SNG variant under songs/bin/generic.</summary>
    Pc,

    /// <summary>The Mac SNG variant under songs/bin/macos.</summary>
    Mac
}
