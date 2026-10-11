using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using Psarc2Tab.Formats.Rocksmith;

namespace Psarc2Tab.Rocksmith.Tests;

/// <summary>Verifies encrypted SNG container decoding and validation.</summary>
public sealed class SngContainerReaderTests
{
    /// <summary>Resolves supported SNG variants from manifest paths.</summary>
    [Theory]
    [InlineData("songs/bin/generic/track.sng", SngPlatform.Pc)]
    [InlineData("songs/bin/macos/track.sng", SngPlatform.Mac)]
    public void GetPlatform_SupportedArchivePath_ReturnsPlatform(string path, SngPlatform expected)
    {
        Assert.Equal(expected, SngContainerReader.GetPlatform(path));
    }

    /// <summary>Rejects SNG paths whose platform cannot be established.</summary>
    [Theory]
    [InlineData("songs/bin/unknown/track.sng")]
    [InlineData("songs/bin/generic/track.xml")]
    public void GetPlatform_UnsupportedArchivePath_ThrowsArgumentException(string path)
    {
        Assert.Throws<ArgumentException>(() => SngContainerReader.GetPlatform(path));
    }

    /// <summary>Decodes a PC container and preserves its chart bytes.</summary>
    [Fact]
    public void Decode_PcContainer_ReturnsInflatedPayload()
    {
        var expected = "synthetic chart payload"u8.ToArray();
        using var source = new MemoryStream(CreateContainer(expected, SngPlatform.Pc));

        var actual = new SngContainerReader().Decode(source, SngPlatform.Pc);

        Assert.Equal(expected, actual);
    }

    /// <summary>Decodes a Mac container using its distinct byte order and key.</summary>
    [Fact]
    public void Decode_MacContainer_ReturnsInflatedPayload()
    {
        var expected = "synthetic Mac chart payload"u8.ToArray();
        using var source = new MemoryStream(CreateContainer(expected, SngPlatform.Mac));

        var actual = new SngContainerReader().Decode(source, SngPlatform.Mac);

        Assert.Equal(expected, actual);
    }

    /// <summary>Rejects an SNG whose platform marker disagrees with the selected platform.</summary>
    [Fact]
    public void Decode_MismatchedPlatformMarker_ThrowsInvalidDataException()
    {
        using var source = new MemoryStream(CreateContainer("chart"u8.ToArray(), SngPlatform.Pc));

        Assert.Throws<InvalidDataException>(() => new SngContainerReader().Decode(source, SngPlatform.Mac));
    }

    /// <summary>Rejects truncated and invalid SNG headers.</summary>
    [Theory]
    [InlineData(new byte[] { 0x4A, 0, 0, 0 })]
    [InlineData(new byte[] { 0, 0, 0, 0, 3, 0, 0, 0 })]
    public void Decode_InvalidHeader_ThrowsInvalidDataException(byte[] bytes)
    {
        using var source = new MemoryStream(bytes);

        Assert.Throws<InvalidDataException>(() => new SngContainerReader().Decode(source, SngPlatform.Pc));
    }

    /// <summary>Rejects decoded sizes that exceed the configured limit.</summary>
    [Fact]
    public void Decode_DeclaredLengthExceedsLimit_ThrowsInvalidDataException()
    {
        var bytes = CreateContainer("chart"u8.ToArray(), SngPlatform.Pc);
        var decodedHeader = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(decodedHeader, (uint)SngContainerReader.MaximumDecodedLength + 1);
        var encoded = EncodeCounter(decodedHeader, PcKey, new byte[16]);
        encoded.CopyTo(bytes, 24);
        using var source = new MemoryStream(bytes);

        Assert.Throws<InvalidDataException>(() => new SngContainerReader().Decode(source, SngPlatform.Pc));
    }

    /// <summary>Rejects encrypted payload bytes that do not form a valid zlib stream.</summary>
    [Fact]
    public void Decode_InvalidZlibPayload_ThrowsInvalidDataException()
    {
        var bytes = CreateContainer("chart"u8.ToArray(), SngPlatform.Pc);
        bytes[28] ^= 0xFF;
        using var source = new MemoryStream(bytes);

        Assert.Throws<InvalidDataException>(() => new SngContainerReader().Decode(source, SngPlatform.Pc));
    }

    /// <summary>Builds an encrypted synthetic container for one platform.</summary>
    /// <param name="payload">The chart bytes to compress.</param>
    /// <param name="platform">The SNG platform variant.</param>
    /// <returns>The complete SNG container.</returns>
    private static byte[] CreateContainer(byte[] payload, SngPlatform platform)
    {
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(payload);
        }

        var mac = platform == SngPlatform.Mac;
        var plaintext = new byte[sizeof(uint) + compressed.Length];
        if (mac)
        {
            BinaryPrimitives.WriteUInt32BigEndian(plaintext, (uint)payload.Length);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(plaintext, (uint)payload.Length);
        }

        compressed.ToArray().CopyTo(plaintext, sizeof(uint));
        var iv = new byte[16];
        var ciphertext = EncodeCounter(plaintext, mac ? MacKey : PcKey, iv);
        var result = new byte[8 + iv.Length + ciphertext.Length + 56];
        if (mac)
        {
            BinaryPrimitives.WriteUInt32BigEndian(result, 0x4A);
            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(4), 1);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(result, 0x4A);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), 3);
        }

        iv.CopyTo(result, 8);
        ciphertext.CopyTo(result, 24);
        return result;
    }

    /// <summary>Encrypts bytes with AES-CTR for a deterministic test fixture.</summary>
    /// <param name="input">The plaintext bytes.</param>
    /// <param name="key">The platform key.</param>
    /// <param name="iv">The initial counter value.</param>
    /// <returns>The encrypted bytes.</returns>
    private static byte[] EncodeCounter(byte[] input, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        using var encryptor = aes.CreateEncryptor(key, new byte[16]);
        var counter = iv.ToArray();
        var mask = new byte[16];
        var result = new byte[input.Length];
        for (var offset = 0; offset < input.Length; offset += 16)
        {
            _ = encryptor.TransformBlock(counter, 0, 16, mask, 0);
            for (var index = 15; index >= 0; index--)
            {
                counter[index]++;
                if (counter[index] != 0)
                {
                    break;
                }
            }

            for (var index = 0; index < Math.Min(16, input.Length - offset); index++)
            {
                result[offset + index] = (byte)(input[offset + index] ^ mask[index]);
            }
        }

        return result;
    }

    /// <summary>The PC SNG test key.</summary>
    private static readonly byte[] PcKey = Convert.FromHexString(
        "CB648DF3D12A16BF71701414E69619EC171CCA5D2A142E3E59DE7ADDA18A3A30");

    /// <summary>The Mac SNG test key.</summary>
    private static readonly byte[] MacKey = Convert.FromHexString(
        "9821330E34B91F70D0A48CBD625993126970CEA09192C0E6CDA676CC9838289D");
}
