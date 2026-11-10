using System.Buffers.Binary;

namespace Psarc2Tab.Formats.Rocksmith;

/// <summary>Decodes Wwise Vorbis audio to the WAV format required by portable feedpak packages.</summary>
public sealed class VgmstreamWwiseAudioDecoder : IWwiseAudioDecoder
{
    /// <summary>The largest compressed WEM payload accepted by the decoder.</summary>
    private const int MaximumWemLength = 64 * 1024 * 1024;

    /// <summary>The largest decoded WAV payload retained in memory.</summary>
    private const long MaximumWaveLength = 512 * 1024 * 1024;

    /// <summary>The process runner used to invoke the external vgmstream decoder.</summary>
    private readonly IVgmstreamRunner _runner;

    /// <summary>Creates a Wwise decoder using vgmstream or the supplied runner.</summary>
    /// <param name="runner">An optional process runner, mainly for deterministic tests.</param>
    public VgmstreamWwiseAudioDecoder(IVgmstreamRunner? runner = null)
    {
        _runner = runner ?? new VgmstreamRunner();
    }

    /// <inheritdoc />
    public byte[] DecodeToWave(ReadOnlyMemory<byte> wemData)
    {
        if (wemData.IsEmpty || wemData.Length > MaximumWemLength)
        {
            throw new ArgumentException("The WEM input is empty or exceeds the supported 64 MiB limit.", nameof(wemData));
        }

        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"psarc2tab-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        var inputPath = Path.Combine(temporaryDirectory, "audio.wem");
        var outputPath = Path.Combine(temporaryDirectory, "audio.wav");
        try
        {
            File.WriteAllBytes(inputPath, wemData.ToArray());
            try
            {
                _runner.Decode(inputPath, outputPath);
            }
            catch (InvalidDataException exception) when (IsWwiseVorbis(wemData.Span))
            {
                throw new InvalidOperationException(
                    "The archive contains Wwise Vorbis audio, but vgmstream could not decode it. Use a vgmstream build with Vorbis/libvorbis support; the WEM may also be malformed or unsupported. " +
                    exception.Message, exception);
            }

            var output = new FileInfo(outputPath);
            if (!output.Exists || output.Length is < 44 or > MaximumWaveLength)
            {
                throw new InvalidDataException("vgmstream did not produce a WAV file within the supported size range.");
            }

            var bytes = File.ReadAllBytes(outputPath);
            if (!IsPcmWave(bytes))
            {
                throw new InvalidDataException("vgmstream produced an unsupported or malformed PCM WAV file.");
            }

            return bytes;
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    /// <summary>Identifies a RIFF/WAVE Wwise Vorbis stream by its format tag.</summary>
    /// <param name="wem">The candidate WEM bytes.</param>
    /// <returns>True when the Wwise format code denotes Vorbis.</returns>
    private static bool IsWwiseVorbis(ReadOnlySpan<byte> wem) =>
        wem.Length >= 22 && wem[..4].SequenceEqual("RIFF"u8) && wem[8..12].SequenceEqual("WAVE"u8) &&
        wem[12..16].SequenceEqual("fmt "u8) && wem[20] == 0xFF && wem[21] == 0xFF;

    /// <summary>Checks the RIFF header and PCM format chunk of decoded audio.</summary>
    /// <param name="wave">The candidate WAV bytes.</param>
    /// <returns>True when the bytes begin with a PCM RIFF/WAVE structure.</returns>
    private static bool IsPcmWave(ReadOnlySpan<byte> wave)
    {
        if (wave.Length < 44 || !wave[..4].SequenceEqual("RIFF"u8) || !wave[8..12].SequenceEqual("WAVE"u8))
        {
            return false;
        }

        var declaredSize = BinaryPrimitives.ReadUInt32LittleEndian(wave[4..8]);
        if ((ulong)declaredSize + 8 > (ulong)wave.Length)
        {
            return false;
        }

        var offset = 12;
        while (offset <= wave.Length - 8)
        {
            var chunkLength = BinaryPrimitives.ReadUInt32LittleEndian(wave[(offset + 4)..(offset + 8)]);
            if ((ulong)offset + 8 + chunkLength > (ulong)wave.Length)
            {
                return false;
            }

            if (wave.Slice(offset, 4).SequenceEqual("fmt "u8))
            {
                if (chunkLength < 16 || BinaryPrimitives.ReadUInt16LittleEndian(wave[(offset + 8)..(offset + 10)]) != 1)
                {
                    return false;
                }
            }
            else if (wave.Slice(offset, 4).SequenceEqual("data"u8))
            {
                return offset > 12;
            }

            offset = checked(offset + 8 + (int)chunkLength + (int)(chunkLength & 1));
        }

        return false;
    }
}
