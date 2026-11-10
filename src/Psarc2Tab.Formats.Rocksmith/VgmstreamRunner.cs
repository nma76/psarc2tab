using System.Diagnostics;

namespace Psarc2Tab.Formats.Rocksmith;

/// <summary>Invokes a user-installed vgmstream CLI, which this project does not redistribute.</summary>
public sealed class VgmstreamRunner : IVgmstreamRunner
{
    /// <summary>Converts a WEM file to WAV without invoking a shell.</summary>
    /// <param name="inputPath">The readable WEM input file.</param>
    /// <param name="outputPath">The target WAV output file.</param>
    /// <exception cref="InvalidDataException">vgmstream reports a conversion failure.</exception>
    /// <exception cref="InvalidOperationException">The executable cannot be started.</exception>
    public void Decode(string inputPath, string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var executable = Environment.GetEnvironmentVariable("PSARC2TAB_VGMSTREAM");
        if (string.IsNullOrWhiteSpace(executable))
        {
            executable = OperatingSystem.IsWindows() ? "vgmstream-cli.exe" : "vgmstream-cli";
        }

        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add(outputPath);
        startInfo.ArgumentList.Add(inputPath);

        Process process;
        try
        {
            process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("The vgmstream process could not be started.");
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            throw new InvalidOperationException(
                "vgmstream was not found. Install vgmstream-cli or set PSARC2TAB_VGMSTREAM to its executable path.", exception);
        }

        using (process)
        {
            var errorTask = process.StandardError.ReadToEndAsync();
            var outputTask = process.StandardOutput.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            try
            {
                process.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException exception)
            {
                process.Kill(entireProcessTree: true);
                throw new InvalidDataException("vgmstream exceeded the five-minute audio decode limit.", exception);
            }

            var standardError = errorTask.GetAwaiter().GetResult().Trim();
            _ = outputTask.GetAwaiter().GetResult();
            if (process.ExitCode != 0)
            {
                var detail = string.IsNullOrEmpty(standardError) ? string.Empty : $" {standardError}";
                throw new InvalidDataException($"vgmstream could not decode the WEM audio (exit code {process.ExitCode}).{detail}");
            }
        }
    }
}
