namespace Psarc2Tab.Cli;

/// <summary>Identifies the command selected by the parsed arguments.</summary>
public enum CommandKind
{
    /// <summary>Requests a conversion using input and output paths.</summary>
    Convert,

    /// <summary>Lists the instrument arrangements in an input archive.</summary>
    ListArrangements,

    /// <summary>Requests the usage text.</summary>
    Help,

    /// <summary>Requests version information.</summary>
    Version,

    /// <summary>Represents invalid or incomplete arguments.</summary>
    Invalid
}

/// <summary>Contains a parsed command and its associated values or diagnostic.</summary>
/// <param name="Kind">The selected command kind.</param>
/// <param name="InputPath">The input archive path, when conversion was requested.</param>
/// <param name="OutputPath">The output tablature path, when conversion was requested.</param>
/// <param name="ArrangementNumber">The one-based arrangement number selected for conversion.</param>
/// <param name="Error">The reason parsing failed, when the command is invalid.</param>
public sealed record ParsedCommand(
    CommandKind Kind,
    string? InputPath = null,
    string? OutputPath = null,
    int? ArrangementNumber = null,
    string? Error = null);

/// <summary>Parses supported command-line flags without performing file or conversion work.</summary>
public sealed class CommandLineParser
{
    /// <summary>Parses the supplied flags into a command or an input error.</summary>
    /// <param name="args">Command-line arguments, excluding the executable name.</param>
    /// <returns>The parsed command and any values or diagnostic.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="args"/> is null.</exception>
    public ParsedCommand Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? inputPath = null;
        string? outputPath = null;
        int? arrangementNumber = null;
        var listArrangements = false;
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
                case "--list":
                    if (listArrangements)
                    {
                        return Invalid("The '--list' option can only be specified once.");
                    }

                    listArrangements = true;
                    break;
                case "--in":
                case "--out":
                case "--arr":
                    if (index + 1 >= args.Count || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        return Invalid(argument == "--arr"
                            ? "Option '--arr' requires a positive number."
                            : $"Option '{argument}' requires a path.");
                    }

                    var path = args[++index];
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        return Invalid($"Option '{argument}' requires a non-empty path.");
                    }

                    if (argument == "--arr")
                    {
                        if (arrangementNumber is not null)
                        {
                            return Invalid("The arrangement option can only be specified once.");
                        }

                        if (!int.TryParse(path, System.Globalization.NumberStyles.None,
                                System.Globalization.CultureInfo.InvariantCulture, out var parsedNumber) || parsedNumber <= 0)
                        {
                            return Invalid("Option '--arr' requires a positive number.");
                        }

                        arrangementNumber = parsedNumber;
                    }
                    else if (argument == "--in")
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

        if ((showHelp || showVersion) && (listArrangements || arrangementNumber is not null))
        {
            return Invalid("Help and version options cannot be combined with archive operations.");
        }

        if (showHelp)
        {
            return new ParsedCommand(CommandKind.Help);
        }

        if (showVersion)
        {
            return new ParsedCommand(CommandKind.Version);
        }

        if (listArrangements)
        {
            if (inputPath is null || outputPath is not null || arrangementNumber is not null)
            {
                return Invalid("'--list' requires '--in' and cannot be combined with '--out' or '--arr'.");
            }

            return new ParsedCommand(CommandKind.ListArrangements, InputPath: inputPath);
        }

        if (inputPath is null || outputPath is null)
        {
            return Invalid("Both '--in' and '--out' are required.");
        }

        return new ParsedCommand(CommandKind.Convert, inputPath, outputPath, arrangementNumber);
    }

    /// <summary>Creates an invalid-command result with a concise diagnostic.</summary>
    /// <param name="error">The reason the arguments are invalid.</param>
    /// <returns>An invalid parsed command.</returns>
    private static ParsedCommand Invalid(string error) => new(CommandKind.Invalid, Error: error);
}
