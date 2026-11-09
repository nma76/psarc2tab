namespace Psarc2Tab.Formats.Rocksmith;

/// <summary>Runs vgmstream against one WEM file and writes its decoded WAV output.</summary>
public interface IVgmstreamRunner
{
    /// <summary>Converts one input WEM path to a WAV output path.</summary>
    /// <param name="inputPath">The readable source WEM file.</param>
    /// <param name="outputPath">The output WAV path to create.</param>
    /// <exception cref="InvalidDataException">The WEM cannot be decoded.</exception>
    /// <exception cref="InvalidOperationException">The vgmstream executable is unavailable.</exception>
    void Decode(string inputPath, string outputPath);
}
