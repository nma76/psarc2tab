namespace Psarc2Tab.Formats.Rocksmith;

/// <summary>Decodes an extracted Wwise media file into a portable PCM WAV stream.</summary>
public interface IWwiseAudioDecoder
{
    /// <summary>Decodes a bounded WEM payload to a complete PCM WAV byte sequence.</summary>
    /// <param name="wemData">The extracted WEM contents.</param>
    /// <returns>The decoded PCM WAV file.</returns>
    /// <exception cref="ArgumentException">The input is empty or exceeds the supported WEM limit.</exception>
    /// <exception cref="InvalidDataException">The decoder rejects the WEM or produces invalid output.</exception>
    /// <exception cref="InvalidOperationException">The required decoder executable is unavailable.</exception>
    byte[] DecodeToWave(ReadOnlyMemory<byte> wemData);
}
