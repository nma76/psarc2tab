using System.Buffers.Binary;

namespace Psarc2Tab.Formats.Feedpak;

/// <summary>Reads the duration of a bounded PCM RIFF/WAVE payload.</summary>
internal readonly record struct WaveFileInfo(double DurationSeconds)
{
    /// <summary>Checks the RIFF chunks and extracts the PCM data duration.</summary>
    /// <param name="wave">The complete WAV file bytes.</param>
    /// <param name="info">The parsed duration when the file is supported.</param>
    /// <returns>True when a PCM format and complete audio data chunk are present.</returns>
    internal static bool TryRead(ReadOnlySpan<byte> wave, out WaveFileInfo info)
    {
        info = default;
        if (wave.Length < 12 || !wave[..4].SequenceEqual("RIFF"u8) || !wave[8..12].SequenceEqual("WAVE"u8))
        {
            return false;
        }

        var declaredLength = (ulong)BinaryPrimitives.ReadUInt32LittleEndian(wave[4..8]) + 8;
        if (declaredLength > (ulong)wave.Length || declaredLength < 12)
        {
            return false;
        }

        uint sampleRate = 0;
        ushort blockAlign = 0;
        ushort encoding = 0;
        uint dataLength = 0;
        var hasFormat = false;
        var hasData = false;
        var offset = 12;
        while ((ulong)offset + 8 <= declaredLength)
        {
            var chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(wave[(offset + 4)..(offset + 8)]);
            var bodyOffset = offset + 8;
            if ((ulong)bodyOffset + chunkSize > declaredLength)
            {
                return false;
            }

            if (wave.Slice(offset, 4).SequenceEqual("fmt "u8))
            {
                if (chunkSize < 16)
                {
                    return false;
                }

                encoding = BinaryPrimitives.ReadUInt16LittleEndian(wave[bodyOffset..(bodyOffset + 2)]);
                blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(wave[(bodyOffset + 12)..(bodyOffset + 14)]);
                sampleRate = BinaryPrimitives.ReadUInt32LittleEndian(wave[(bodyOffset + 4)..(bodyOffset + 8)]);
                hasFormat = true;
            }
            else if (wave.Slice(offset, 4).SequenceEqual("data"u8))
            {
                dataLength = chunkSize;
                hasData = true;
            }

            offset = checked(bodyOffset + (int)chunkSize + (int)(chunkSize & 1));
        }

        if (!hasFormat || !hasData || encoding != 1 || sampleRate == 0 || blockAlign == 0 || dataLength % blockAlign != 0)
        {
            return false;
        }

        info = new WaveFileInfo((double)(dataLength / blockAlign) / sampleRate);
        return double.IsFinite(info.DurationSeconds) && info.DurationSeconds > 0;
    }
}
