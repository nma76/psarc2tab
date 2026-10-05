using Psarc2Tab.Cli;

namespace Psarc2Tab.Cli.Tests;

public sealed class CliApplicationTests
{
    [Fact]
    public void Run_Help_PrintsUsageAndReturnsSuccess()
    {
        var output = Run("--help");

        Assert.Equal(CliApplication.SuccessExitCode, output.ExitCode);
        Assert.Contains("--in <path>", output.StandardOutput);
        Assert.Contains("--version", output.StandardOutput);
        Assert.Empty(output.StandardError);
    }

    [Fact]
    public void Run_Version_PrintsVersionAndReturnsSuccess()
    {
        var output = Run("--version");

        Assert.Equal(CliApplication.SuccessExitCode, output.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(output.StandardOutput));
    }

    [Fact]
    public void Run_InvalidArguments_PrintsDiagnosticToStandardError()
    {
        var output = Run("--in", "song.psarc");

        Assert.Equal(CliApplication.InvalidArgumentsExitCode, output.ExitCode);
        Assert.Contains("Both '--in' and '--out' are required", output.StandardError);
        Assert.Empty(output.StandardOutput);
    }

    [Fact]
    public void Run_MissingInput_ReturnsInputError()
    {
        using var directory = new TemporaryDirectory();
        var output = Run("--in", Path.Combine(directory.Path, "missing.psarc"), "--out", Path.Combine(directory.Path, "song.tab"));

        Assert.Equal(CliApplication.InputErrorExitCode, output.ExitCode);
        Assert.Contains("Cannot read input file", output.StandardError);
    }

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

    private static RunResult Run(params string[] args)
    {
        using var standardOutput = new StringWriter();
        using var standardError = new StringWriter();
        var exitCode = new CliApplication().Run(args, standardOutput, standardError);
        return new RunResult(exitCode, standardOutput.ToString(), standardError.ToString());
    }

    private sealed record RunResult(int ExitCode, string StandardOutput, string StandardError);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"psarc2tab-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
