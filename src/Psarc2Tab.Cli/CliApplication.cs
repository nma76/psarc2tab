using System.Reflection;
using System.Text;
using Psarc2Tab.Application;
using Psarc2Tab.Domain;
using Psarc2Tab.Formats.Rocksmith;
using Psarc2Tab.Tablature;

namespace Psarc2Tab.Cli;

/// <summary>Coordinates command-line requests, diagnostics, and arrangement conversion.</summary>
public sealed class CliApplication
{
    /// <summary>Exit code returned after displaying help, version, or completed conversion.</summary>
    public const int SuccessExitCode = 0;

    /// <summary>Exit code returned when command-line arguments are invalid.</summary>
    public const int InvalidArgumentsExitCode = 2;

    /// <summary>Exit code returned when the input archive cannot be read.</summary>
    public const int InputErrorExitCode = 3;

    /// <summary>Exit code returned when the output destination cannot be written.</summary>
    public const int OutputErrorExitCode = 4;

    /// <summary>Exit code returned when imported data cannot be exported.</summary>
    public const int ConversionErrorExitCode = 5;

    /// <summary>Exit code returned when an arrangement number is not present in the archive.</summary>
    public const int InvalidArrangementSelectionExitCode = 6;

    /// <summary>Parses command-line flags for this application.</summary>
    private readonly CommandLineParser _parser;

    /// <summary>Reads arrangement listings for list and selection commands.</summary>
    private readonly RocksmithArrangementCatalog _arrangementCatalog;

    /// <summary>Imports selected arrangements and resolves their output formats.</summary>
    private readonly ExportArrangementUseCase _exportArrangement;

    /// <summary>Imports and writes the archive's timed lyrics.</summary>
    private readonly ExportLyricsUseCase _exportLyrics;

    /// <summary>Creates a CLI application with supplied components or their defaults.</summary>
    /// <param name="parser">An optional parser for command-line flags.</param>
    /// <param name="arrangementCatalog">An optional archive arrangement catalog.</param>
    /// <param name="exportArrangement">An optional arrangement export use case.</param>
    public CliApplication(
        CommandLineParser? parser = null,
        RocksmithArrangementCatalog? arrangementCatalog = null,
        ExportArrangementUseCase? exportArrangement = null,
        ExportLyricsUseCase? exportLyrics = null)
    {
        _parser = parser ?? new CommandLineParser();
        _arrangementCatalog = arrangementCatalog ?? new RocksmithArrangementCatalog();
        _exportArrangement = exportArrangement ?? new ExportArrangementUseCase(
            new RocksmithArchiveArrangementImporter(_arrangementCatalog, new RocksmithArrangementImporter()),
            new ArrangementFormatRegistry([new TablatureTextWriter()]));
        _exportLyrics = exportLyrics ?? new ExportLyricsUseCase(
            new RocksmithArchiveLyricsImporter(_arrangementCatalog), new LrcLyricsWriter());
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
            case CommandKind.ListArrangements:
                return ListArrangements(command, standardOutput, standardError);
            case CommandKind.Invalid:
                standardError.WriteLine($"Error: {command.Error}");
                standardError.WriteLine("Run 'psarc2tab --help' for usage.");
                return InvalidArgumentsExitCode;
            case CommandKind.Convert:
                return ValidateAndConvert(command, standardOutput, standardError);
            default:
                throw new InvalidOperationException($"Unsupported command kind: {command.Kind}.");
        }
    }

    /// <summary>Prints the numbered instrument arrangements in an input archive.</summary>
    /// <param name="command">A parsed arrangement-list request.</param>
    /// <param name="standardOutput">Writer for the arrangement listing.</param>
    /// <param name="standardError">Writer for input diagnostics.</param>
    /// <returns>The success or input-error exit code.</returns>
    private int ListArrangements(ParsedCommand command, TextWriter standardOutput, TextWriter standardError)
    {
        try
        {
            using var archive = File.OpenRead(command.InputPath!);
            foreach (var arrangement in _arrangementCatalog.ListItems(archive))
            {
                standardOutput.WriteLine($"{arrangement.Number}. {arrangement.Name} — {arrangement.ArchivePath}");
            }

            return SuccessExitCode;
        }
        catch (Exception exception) when (IsInputFailure(exception))
        {
            standardError.WriteLine($"Error: Cannot list arrangements in '{command.InputPath}': {exception.Message}");
            return InputErrorExitCode;
        }
    }

    /// <summary>Imports the selected arrangement and safely writes its tablature document.</summary>
    /// <param name="command">A parsed conversion request.</param>
    /// <param name="standardOutput">Writer for conversion success output.</param>
    /// <param name="standardError">Writer for the conversion diagnostic.</param>
    /// <returns>The input, output, selection, or conversion error exit code.</returns>
    private int ValidateAndConvert(ParsedCommand command, TextWriter standardOutput, TextWriter standardError)
    {
        if (!_exportArrangement.SupportsFormat(command.FormatName))
        {
            var supportedFormats = string.Join(", ", _exportArrangement.SupportedFormatNames);
            standardError.WriteLine(
                $"Error: Unsupported output format '{command.FormatName}'. Supported formats: {supportedFormats}.");
            return InvalidArgumentsExitCode;
        }

        if (command.AllArrangements)
        {
            return ExportAllArrangements(command, standardOutput, standardError);
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

        Song? song = null;
        SongLyrics? lyrics = null;
        try
        {
            using var input = File.OpenRead(command.InputPath!);
            var arrangementNumber = command.ArrangementNumber ?? 1;
            var items = _arrangementCatalog.ListItems(input);
            if (arrangementNumber > items.Count)
            {
                throw new ArrangementNotFoundException(arrangementNumber);
            }

            input.Position = 0;
            if (items[arrangementNumber - 1].IsLyrics)
            {
                lyrics = _exportLyrics.Import(input);
            }
            else
            {
                song = _exportArrangement.Import(input, arrangementNumber);
            }
        }
        catch (ArrangementNotFoundException exception)
        {
            standardError.WriteLine(
                $"Error: {exception.Message} Run 'psarc2tab --in <input.psarc> --list' to see valid numbers.");
            return InvalidArrangementSelectionExitCode;
        }
        catch (InvalidOperationException exception)
        {
            standardError.WriteLine($"Error: Cannot export selected archive item: {exception.Message}");
            return ConversionErrorExitCode;
        }
        catch (Exception exception) when (IsInputFailure(exception))
        {
            standardError.WriteLine($"Error: Cannot read input file or import arrangement from '{command.InputPath}': {exception.Message}");
            return InputErrorExitCode;
        }

        try
        {
            if (lyrics is not null)
            {
                WriteLyricsSafely(lyrics, command.OutputPath!);
                standardOutput.WriteLine($"Wrote lyrics to '{command.OutputPath}'.");
            }
            else
            {
                WriteOutputSafely(song ?? throw new InvalidOperationException("No archive item was imported."),
                    command.OutputPath!, command.FormatName);
                standardOutput.WriteLine($"Wrote {command.FormatName} export to '{command.OutputPath}'.");
            }

            return SuccessExitCode;
        }
        catch (InvalidOperationException exception)
        {
            standardError.WriteLine($"Error: Cannot export selected archive item: {exception.Message}");
            return ConversionErrorExitCode;
        }
        catch (Exception exception) when (IsOutputFailure(exception))
        {
            standardError.WriteLine($"Error: Cannot write output file '{command.OutputPath}': {exception.Message}");
            return OutputErrorExitCode;
        }
    }

    /// <summary>Imports every instrument arrangement and commits its output files as one set.</summary>
    /// <param name="command">A request selecting all instrument arrangements.</param>
    /// <param name="standardOutput">Writer for successful output paths.</param>
    /// <param name="standardError">Writer for diagnostics.</param>
    /// <returns>The success or relevant input, output, or conversion error code.</returns>
    private int ExportAllArrangements(ParsedCommand command, TextWriter standardOutput, TextWriter standardError)
    {
        RocksmithArchiveItem[] arrangements;
        try
        {
            using var input = File.OpenRead(command.InputPath!);
            arrangements = _arrangementCatalog.ListItems(input).Where(item => !item.IsLyrics).ToArray();
            if (arrangements.Length == 0)
            {
                throw new InvalidDataException("The archive contains no instrument arrangements to export.");
            }
        }
        catch (Exception exception) when (IsInputFailure(exception))
        {
            standardError.WriteLine($"Error: Cannot read input file or list arrangements from '{command.InputPath}': {exception.Message}");
            return InputErrorExitCode;
        }

        string[] outputPaths;
        try
        {
            outputPaths = CreateArrangementOutputPaths(command.OutputPath!, arrangements);
            ValidateOutputSet(outputPaths);
        }
        catch (InvalidDataException exception)
        {
            standardError.WriteLine($"Error: Cannot create arrangement output names: {exception.Message}");
            return InputErrorExitCode;
        }
        catch (Exception exception) when (IsOutputFailure(exception))
        {
            standardError.WriteLine($"Error: Cannot prepare output files based on '{command.OutputPath}': {exception.Message}");
            return OutputErrorExitCode;
        }

        var exports = new List<(Song Song, string OutputPath)>(arrangements.Length);
        try
        {
            using var input = File.OpenRead(command.InputPath!);
            for (var index = 0; index < arrangements.Length; index++)
            {
                input.Position = 0;
                var song = _exportArrangement.Import(input, arrangements[index].Number);
                exports.Add((song, outputPaths[index]));
            }
        }
        catch (ArrangementNotFoundException exception)
        {
            standardError.WriteLine($"Error: {exception.Message}");
            return InvalidArrangementSelectionExitCode;
        }
        catch (InvalidOperationException exception)
        {
            standardError.WriteLine($"Error: Cannot export all arrangements: {exception.Message}");
            return ConversionErrorExitCode;
        }
        catch (Exception exception) when (IsInputFailure(exception))
        {
            standardError.WriteLine($"Error: Cannot import arrangements from '{command.InputPath}': {exception.Message}");
            return InputErrorExitCode;
        }

        try
        {
            WriteOutputSetSafely(exports, command.FormatName);
            foreach (var export in exports)
            {
                standardOutput.WriteLine($"Wrote {command.FormatName} export to '{export.OutputPath}'.");
            }

            return SuccessExitCode;
        }
        catch (InvalidOperationException exception)
        {
            standardError.WriteLine($"Error: Cannot export all arrangements: {exception.Message}");
            return ConversionErrorExitCode;
        }
        catch (Exception exception) when (IsOutputFailure(exception))
        {
            standardError.WriteLine($"Error: Cannot write all arrangement outputs: {exception.Message}");
            return OutputErrorExitCode;
        }
    }

    /// <summary>Builds collision-free suffixed paths in archive order.</summary>
    /// <param name="outputPath">The requested output base path.</param>
    /// <param name="arrangements">The instrument arrangements to name.</param>
    /// <returns>One full output path for each arrangement.</returns>
    /// <exception cref="InvalidDataException">An arrangement name cannot be used as an output suffix.</exception>
    private static string[] CreateArrangementOutputPaths(string outputPath,
        IReadOnlyList<RocksmithArchiveItem> arrangements)
    {
        var fullBasePath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullBasePath)
            ?? throw new DirectoryNotFoundException("The output directory does not exist.");
        var fileName = Path.GetFileName(fullBasePath);
        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(extension))
        {
            extension = ".tab";
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var occurrences = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var paths = new string[arrangements.Count];
        for (var index = 0; index < arrangements.Count; index++)
        {
            var name = arrangements[index].Name.ToLowerInvariant();
            if (name.Length == 0 || name.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
            {
                throw new InvalidDataException("An arrangement name cannot be used in an output filename.");
            }

            occurrences.TryGetValue(name, out var occurrence);
            occurrence++;
            occurrences[name] = occurrence;
            var occurrenceSuffix = occurrence == 1 ? string.Empty : $"-{occurrence}";
            paths[index] = Path.Combine(directory, $"{stem}-{name}{occurrenceSuffix}{extension}");
        }

        return paths;
    }

    /// <summary>Prevalidates every destination and rejects duplicate paths case-insensitively.</summary>
    /// <param name="outputPaths">The complete output path set.</param>
    /// <exception cref="IOException">Two outputs collide or a destination already exists.</exception>
    private static void ValidateOutputSet(IEnumerable<string> outputPaths)
    {
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var outputPath in outputPaths)
        {
            var fullPath = Path.GetFullPath(outputPath);
            if (!seenPaths.Add(fullPath))
            {
                throw new IOException("Multiple arrangements resolve to the same output path.");
            }

            ValidateOutputDestination(fullPath);
        }
    }

    /// <summary>Stages all outputs before moving them into place and removes the set on failure.</summary>
    /// <param name="exports">The imported arrangements and their final paths.</param>
    /// <param name="formatName">The registered output format.</param>
    /// <exception cref="IOException">A destination cannot be written or a staged move fails.</exception>
    /// <exception cref="UnauthorizedAccessException">A destination cannot be accessed.</exception>
    private void WriteOutputSetSafely(IReadOnlyList<(Song Song, string OutputPath)> exports, string formatName)
    {
        var stagedFiles = new List<(string TemporaryPath, string OutputPath)>();
        var committedPaths = new List<string>();
        try
        {
            foreach (var export in exports)
            {
                var outputPath = Path.GetFullPath(export.OutputPath);
                var directory = Path.GetDirectoryName(outputPath)
                    ?? throw new DirectoryNotFoundException("The output directory does not exist.");
                var temporaryPath = Path.Combine(directory, $".psarc2tab-{Guid.NewGuid():N}.tmp");
                using var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                stagedFiles.Add((temporaryPath, outputPath));
                _exportArrangement.Write(export.Song, formatName, stream);
                stream.Flush(flushToDisk: true);
            }

            foreach (var stagedFile in stagedFiles)
            {
                File.Move(stagedFile.TemporaryPath, stagedFile.OutputPath);
                committedPaths.Add(stagedFile.OutputPath);
            }
        }
        catch (Exception exportFailure)
        {
            var cleanupFailures = new List<Exception>();
            foreach (var path in committedPaths)
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    cleanupFailures.Add(exception);
                }
            }

            foreach (var stagedFile in stagedFiles)
            {
                try
                {
                    if (File.Exists(stagedFile.TemporaryPath))
                    {
                        File.Delete(stagedFile.TemporaryPath);
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    cleanupFailures.Add(exception);
                }
            }

            if (cleanupFailures.Count > 0)
            {
                throw new IOException("The export failed and rollback could not remove all staged outputs.",
                    new AggregateException([exportFailure, .. cleanupFailures]));
            }

            throw;
        }
    }

    /// <summary>Identifies expected input and archive-format failures.</summary>
    /// <param name="exception">The failure to classify.</param>
    /// <returns><see langword="true"/> when the exception should be reported as an input error.</returns>
    private static bool IsInputFailure(Exception exception) =>
        exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException or
            System.Security.Cryptography.CryptographicException;

    /// <summary>Identifies expected output path, access, and write failures.</summary>
    /// <param name="exception">The failure to classify.</param>
    /// <returns><see langword="true"/> when the exception should be reported as an output error.</returns>
    private static bool IsOutputFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;

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

    /// <summary>Writes through the selected format writer to a temporary sibling file before moving it into place.</summary>
    /// <param name="song">The imported song to export.</param>
    /// <param name="outputPath">The requested output path.</param>
    /// <param name="formatName">The selected registered output format.</param>
    /// <exception cref="IOException">The destination already exists or writing failed.</exception>
    /// <exception cref="UnauthorizedAccessException">The destination directory is not writable.</exception>
    private void WriteOutputSafely(Song song, string outputPath, string formatName)
    {
        var fullPath = Path.GetFullPath(outputPath);
        if (File.Exists(fullPath) || Directory.Exists(fullPath))
        {
            throw new IOException("The destination already exists. Existing files are never overwritten.");
        }

        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new DirectoryNotFoundException("The destination directory does not exist.");
        var temporaryPath = Path.Combine(directory, $".psarc2tab-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                _exportArrangement.Write(song, formatName, stream);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, fullPath);
        }
        catch
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            throw;
        }
    }

    /// <summary>Writes lyrics to a temporary sibling file and moves it into place without overwriting.</summary>
    /// <param name="lyrics">The imported lyrics to export.</param>
    /// <param name="outputPath">The requested output path.</param>
    /// <exception cref="IOException">The destination already exists or writing failed.</exception>
    /// <exception cref="UnauthorizedAccessException">The destination directory is not writable.</exception>
    private void WriteLyricsSafely(SongLyrics lyrics, string outputPath)
    {
        var fullPath = Path.GetFullPath(outputPath);
        if (File.Exists(fullPath) || Directory.Exists(fullPath))
        {
            throw new IOException("The destination already exists. Existing files are never overwritten.");
        }

        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new DirectoryNotFoundException("The destination directory does not exist.");
        var temporaryPath = Path.Combine(directory, $".psarc2tab-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                _exportLyrics.Write(lyrics, writer);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, fullPath);
        }
        catch
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            throw;
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
          psarc2tab --in <input.psarc> --out <output.tab> --format tab
          psarc2tab --in <input.psarc> --out <output.lrc> --arr <lyrics-number>
          psarc2tab --in <input.psarc> --list
          psarc2tab --in <input.psarc> --out <output.tab> --arr <number>
          psarc2tab --in <input.psarc> --out <output.tab> --arr all
          psarc2tab --help
          psarc2tab --version

        Options:
          --in <path>        Input PSARC archive
          --out <path>       Output file (must not already exist)
          --list             List numbered arrangements and lyrics in the archive
          --arr <n|all>      Select one item, or export every instrument arrangement (default: 1)
          --format <name>    Arrangement output format (supported: tab; default: tab)
          --help             Show this help
          --version          Show version information

        Exit codes:
          0  Help, version, or conversion completed
          2  Invalid arguments
          3  Input file cannot be read
          4  Output destination cannot be written
          5  Arrangement could not be exported
          6  Arrangement number is not available
        """;
}
