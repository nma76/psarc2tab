using System.Reflection;

namespace Psarc2Tab.Cli;

/// <summary>Coordinates argument handling, CLI diagnostics, and conversion validation.</summary>
public sealed class CliApplication
{
    /// <summary>Exit code returned after successfully displaying help or version information.</summary>
    public const int SuccessExitCode = 0;

    /// <summary>Exit code returned when command-line arguments are invalid.</summary>
    public const int InvalidArgumentsExitCode = 2;

    /// <summary>Exit code returned when the input archive cannot be read.</summary>
    public const int InputErrorExitCode = 3;

    /// <summary>Exit code returned when the output destination cannot be written.</summary>
    public const int OutputErrorExitCode = 4;

    /// <summary>Exit code returned when conversion is not implemented.</summary>
    public const int ConversionUnavailableExitCode = 5;

    /// <summary>Parses command-line flags for this application.</summary>
    private readonly CommandLineParser _parser;

    /// <summary>Creates a CLI application with the supplied parser or a default parser.</summary>
    /// <param name="parser">An optional parser for command-line flags.</param>
    public CliApplication(CommandLineParser? parser = null)
    {
        _parser = parser ?? new CommandLineParser();
    }

    /// <summary>Parses arguments, writes output or diagnostics, and returns the CLI exit code.</summary>
    /// <param name="args">Arguments supplied to the CLI.</param>
    /// <param name="standardOutput">Writer for normal output.</param>
    /// <param name="standardError">Writer for diagnostics.</param>
    /// <returns>A documented CLI exit code.</returns>
    /// <exception cref="ArgumentNullException">An argument or writer is null.</exception>
    public int Run(string[] args, TextWriter standardOutput, TextWriter standardError)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);

        var command = _parser.Parse(args);
        switch (command.Kind)
        {
            case CommandKind.Help:
                standardOutput.WriteLine(HelpText);
                return SuccessExitCode;
            case CommandKind.Version:
                standardOutput.WriteLine(GetVersion());
                return SuccessExitCode;
            case CommandKind.Invalid:
                standardError.WriteLine($"Error: {command.Error}");
                standardError.WriteLine("Run 'psarc2tab --help' for usage.");
                return InvalidArgumentsExitCode;
            case CommandKind.Convert:
                return ValidateAndConvert(command, standardError);
            default:
                throw new InvalidOperationException($"Unsupported command kind: {command.Kind}.");
        }
    }

    /// <summary>Checks conversion paths and reports that conversion is not yet available.</summary>
    /// <param name="command">A parsed conversion request.</param>
    /// <param name="standardError">Writer for the conversion diagnostic.</param>
    /// <returns>The input, output, or conversion-unavailable exit code.</returns>
    private static int ValidateAndConvert(ParsedCommand command, TextWriter standardError)
    {
        try
        {
            using var input = File.OpenRead(command.InputPath!);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            standardError.WriteLine($"Error: Cannot read input file '{command.InputPath}': {exception.Message}");
            return InputErrorExitCode;
        }

        try
        {
            ValidateOutputDestination(command.OutputPath!);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            standardError.WriteLine($"Error: Cannot write output file '{command.OutputPath}': {exception.Message}");
            return OutputErrorExitCode;
        }

        standardError.WriteLine("Error: PSARC to tablature conversion is not implemented yet.");
        return ConversionUnavailableExitCode;
    }

    /// <summary>Verifies that the output path is absent and its directory can create a file.</summary>
    /// <param name="outputPath">The requested output path.</param>
    /// <exception cref="IOException">The destination already exists or is a directory.</exception>
    /// <exception cref="DirectoryNotFoundException">The destination directory does not exist.</exception>
    /// <exception cref="UnauthorizedAccessException">The destination directory is not writable.</exception>
    private static void ValidateOutputDestination(string outputPath)
    {
        var fullPath = Path.GetFullPath(outputPath);
        if (File.Exists(fullPath))
        {
            throw new IOException("The destination already exists. Existing files are never overwritten.");
        }

        if (Directory.Exists(fullPath))
        {
            throw new IOException("The destination path is a directory.");
        }

        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException("The destination directory does not exist.");
        }

        var probePath = Path.Combine(directory, $".psarc2tab-{Guid.NewGuid():N}.tmp");
        try
        {
            using var probe = new FileStream(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        }
        finally
        {
            if (File.Exists(probePath))
            {
                File.Delete(probePath);
            }
        }
    }

    /// <summary>Returns the version embedded in the CLI assembly.</summary>
    /// <returns>A three-part version string, or <c>0.0.0</c> when unavailable.</returns>
    private static string GetVersion() =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>Displays the CLI usage, options, and exit codes.</summary>
    private const string HelpText = """
        psarc2tab - convert a Rocksmith PSARC archive to guitar tablature

        Usage:
          psarc2tab --in <input.psarc> --out <output.tab>
          psarc2tab -in <input.psarc> -out <output.tab>
          psarc2tab --help
          psarc2tab --version

        Options:
          --in, -in <path>   Input PSARC archive
          --out, -out <path> Output tablature file (must not already exist)
          --help        Show this help
          --version     Show version information

        Exit codes:
          0  Help or version displayed
          2  Invalid arguments
          3  Input file cannot be read
          4  Output destination cannot be written
          5  Conversion is not implemented
        """;
}
