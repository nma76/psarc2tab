using System.Reflection;

namespace Psarc2Tab.Cli;

public sealed class CliApplication
{
    public const int SuccessExitCode = 0;
    public const int InvalidArgumentsExitCode = 2;
    public const int InputErrorExitCode = 3;
    public const int OutputErrorExitCode = 4;
    public const int ConversionUnavailableExitCode = 5;

    private readonly CommandLineParser _parser;

    public CliApplication(CommandLineParser? parser = null)
    {
        _parser = parser ?? new CommandLineParser();
    }

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

    private static string GetVersion() =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    private const string HelpText = """
        psarc2tab - convert a Rocksmith PSARC archive to guitar tablature

        Usage:
          psarc2tab --in <input.psarc> --out <output.tab>
          psarc2tab --help
          psarc2tab --version

        Options:
          --in <path>   Input PSARC archive
          --out <path>  Output tablature file (must not already exist)
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
