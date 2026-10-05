namespace Psarc2Tab.Cli;

public enum CommandKind
{
    Convert,
    Help,
    Version,
    Invalid
}

public sealed record ParsedCommand(CommandKind Kind, string? InputPath = null, string? OutputPath = null, string? Error = null);

public sealed class CommandLineParser
{
    public ParsedCommand Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? inputPath = null;
        string? outputPath = null;
        var showHelp = false;
        var showVersion = false;

        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--help":
                    showHelp = true;
                    break;
                case "--version":
                    showVersion = true;
                    break;
                case "--in":
                case "-in":
                case "--out":
                case "-out":
                    if (index + 1 >= args.Count || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        return Invalid($"Option '{argument}' requires a path.");
                    }

                    var path = args[++index];
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        return Invalid($"Option '{argument}' requires a non-empty path.");
                    }

                    if (argument is "--in" or "-in")
                    {
                        if (inputPath is not null)
                        {
                            return Invalid("The input option can only be specified once.");
                        }

                        inputPath = path;
                    }
                    else
                    {
                        if (outputPath is not null)
                        {
                            return Invalid("The output option can only be specified once.");
                        }

                        outputPath = path;
                    }

                    break;
                default:
                    return Invalid($"Unknown option '{argument}'.");
            }
        }

        if (showHelp && showVersion)
        {
            return Invalid("Options '--help' and '--version' cannot be used together.");
        }

        if (showHelp)
        {
            return new ParsedCommand(CommandKind.Help);
        }

        if (showVersion)
        {
            return new ParsedCommand(CommandKind.Version);
        }

        if (inputPath is null || outputPath is null)
        {
            return Invalid("Both '--in' and '--out' are required.");
        }

        return new ParsedCommand(CommandKind.Convert, inputPath, outputPath);
    }

    private static ParsedCommand Invalid(string error) => new(CommandKind.Invalid, Error: error);
}
