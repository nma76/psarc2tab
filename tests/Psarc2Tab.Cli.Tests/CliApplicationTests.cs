using Psarc2Tab.Cli;

namespace Psarc2Tab.Cli.Tests;

/// <summary>Verifies CLI output, exit codes, and filesystem validation.</summary>
public sealed class CliApplicationTests
{
    /// <summary>Prints usage to standard output for the help flag.</summary>
    [Fact]
    public void Run_Help_PrintsUsageAndReturnsSuccess()
    {
        var output = Run("--help");

        Assert.Equal(CliApplication.SuccessExitCode, output.ExitCode);
        Assert.Contains("--in, -in <path>", output.StandardOutput);
        Assert.Contains("--version", output.StandardOutput);
        Assert.Contains("--list", output.StandardOutput);
        Assert.Contains("--arrangement <n>", output.StandardOutput);
        Assert.Empty(output.StandardError);
    }

    /// <summary>Prints version information to standard output.</summary>
    [Fact]
    public void Run_Version_PrintsVersionAndReturnsSuccess()
    {
        var output = Run("--version");

        Assert.Equal(CliApplication.SuccessExitCode, output.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(output.StandardOutput));
    }

    /// <summary>Reports missing required options to standard error.</summary>
    [Fact]
    public void Run_InvalidArguments_PrintsDiagnosticToStandardError()
    {
        var output = Run("--in", "song.psarc");

        Assert.Equal(CliApplication.InvalidArgumentsExitCode, output.ExitCode);
        Assert.Contains("Both '--in' and '--out' are required", output.StandardError);
        Assert.Empty(output.StandardOutput);
    }

    /// <summary>Returns an input error when the requested archive does not exist.</summary>
    [Fact]
    public void Run_MissingInput_ReturnsInputError()
    {
        using var directory = new TemporaryDirectory();
        var output = Run("--in", Path.Combine(directory.Path, "missing.psarc"), "--out", Path.Combine(directory.Path, "song.tab"));

        Assert.Equal(CliApplication.InputErrorExitCode, output.ExitCode);
        Assert.Contains("Cannot read input file", output.StandardError);
    }

    /// <summary>Returns an input error when an arrangement listing archive is missing.</summary>
    [Fact]
    public void Run_ListWithMissingInput_ReturnsInputError()
    {
        using var directory = new TemporaryDirectory();
        var output = Run("--in", Path.Combine(directory.Path, "missing.psarc"), "--list");

        Assert.Equal(CliApplication.InputErrorExitCode, output.ExitCode);
        Assert.Contains("Cannot list arrangements", output.StandardError);
        Assert.Empty(output.StandardOutput);
    }

    /// <summary>Reports malformed archive data as an input error when listing arrangements.</summary>
    [Fact]
    public void Run_ListWithMalformedInput_ReturnsInputError()
    {
        using var directory = new TemporaryDirectory();
        var inputPath = Path.Combine(directory.Path, "malformed.psarc");
        File.WriteAllText(inputPath, "not a PSARC archive");

        var output = Run("--in", inputPath, "--list");

        Assert.Equal(CliApplication.InputErrorExitCode, output.ExitCode);
        Assert.Contains("PSARC header is truncated", output.StandardError);
        Assert.Empty(output.StandardOutput);
    }

    /// <summary>Rejects an existing output without modifying its contents.</summary>
    [Fact]
    public void Run_ExistingOutput_ReturnsOutputErrorWithoutOverwriting()
    {
        using var directory = new TemporaryDirectory();
        var inputPath = Path.Combine(directory.Path, "song.psarc");
        var outputPath = Path.Combine(directory.Path, "song.tab");
        File.WriteAllText(inputPath, "input");
        File.WriteAllText(outputPath, "keep me");

        var output = Run("--in", inputPath, "--out", outputPath);

        Assert.Equal(CliApplication.OutputErrorExitCode, output.ExitCode);
        Assert.Equal("keep me", File.ReadAllText(outputPath));
    }

    /// <summary>Rejects a directory used as the output file path.</summary>
    [Fact]
    public void Run_OutputPathIsDirectory_ReturnsOutputError()
    {
        using var directory = new TemporaryDirectory();
        var inputPath = Path.Combine(directory.Path, "song.psarc");
        File.WriteAllText(inputPath, "input");

        var output = Run("--in", inputPath, "--out", directory.Path);

        Assert.Equal(CliApplication.OutputErrorExitCode, output.ExitCode);
        Assert.Contains("destination path is a directory", output.StandardError);
    }

    /// <summary>Reports unavailable conversion without creating an output file.</summary>
    [Fact]
    public void Run_ValidPaths_ReportsConversionUnavailableAndCreatesNoOutput()
    {
        using var directory = new TemporaryDirectory();
        var inputPath = Path.Combine(directory.Path, "song.psarc");
        var outputPath = Path.Combine(directory.Path, "song.tab");
        File.WriteAllText(inputPath, "input");

        var output = Run("--in", inputPath, "--out", outputPath);

        Assert.Equal(CliApplication.ConversionUnavailableExitCode, output.ExitCode);
        Assert.Contains("conversion is not implemented", output.StandardError);
        Assert.False(File.Exists(outputPath));
        Assert.Empty(Directory.GetFiles(directory.Path, ".psarc2tab-*.tmp"));
    }

    /// <summary>Runs the CLI with isolated standard output and error writers.</summary>
    /// <param name="args">The command-line arguments to execute.</param>
    /// <returns>The exit code and captured output streams.</returns>
    private static RunResult Run(params string[] args)
    {
        using var standardOutput = new StringWriter();
        using var standardError = new StringWriter();
        var exitCode = new CliApplication().Run(args, standardOutput, standardError);
        return new RunResult(exitCode, standardOutput.ToString(), standardError.ToString());
    }

    /// <summary>Stores the result of a CLI invocation.</summary>
    /// <param name="ExitCode">The process exit code.</param>
    /// <param name="StandardOutput">Captured normal output.</param>
    /// <param name="StandardError">Captured diagnostics.</param>
    private sealed record RunResult(int ExitCode, string StandardOutput, string StandardError);

    /// <summary>Creates and removes an isolated temporary directory for a test.</summary>
    private sealed class TemporaryDirectory : IDisposable
    {
        /// <summary>Creates a uniquely named temporary directory.</summary>
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"psarc2tab-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        /// <summary>Gets the path of the temporary directory.</summary>
        public string Path { get; }

        /// <summary>Deletes the temporary directory and its contents.</summary>
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
