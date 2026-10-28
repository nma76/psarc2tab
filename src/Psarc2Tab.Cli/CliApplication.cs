using System.Reflection;
using System.Text;
using Psarc2Tab.Application;
using Psarc2Tab.Domain;
using Psarc2Tab.Formats.Rocksmith;
using Psarc2Tab.Midi;
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

    /// <summary>Writes MIDI with the optional synthetic percussion track.</summary>
    private readonly ExportArrangementUseCase _exportArrangementWithDrums;

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
        var importer = new RocksmithArchiveArrangementImporter(_arrangementCatalog, new RocksmithArrangementImporter());
        _exportArrangement = exportArrangement ?? new ExportArrangementUseCase(importer,
            new ArrangementFormatRegistry([
                new TablatureTextWriter(), new StandardMidiFileWriter(), new FretboardHeatmapTextWriter()
            ]));
        _exportArrangementWithDrums = new ExportArrangementUseCase(importer,
            new ArrangementFormatRegistry([new StandardMidiFileWriter(includeDrums: true)]));
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
            case CommandKind.Statistics:
                return ShowStatistics(command, standardOutput, standardError);
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

    /// <summary>Prints archive metadata and statistics for all or one selected arrangement.</summary>
    /// <param name="command">The parsed statistics request.</param>
    /// <param name="standardOutput">Writer for statistics.</param>
    /// <param name="standardError">Writer for input diagnostics.</param>
    /// <returns>The success or input-error exit code.</returns>
    private int ShowStatistics(ParsedCommand command, TextWriter standardOutput, TextWriter standardError)
    {
        try
        {
            using var archive = File.OpenRead(command.InputPath!);
            var details = _arrangementCatalog.Inspect(archive);
            archive.Position = 0;
            var items = _arrangementCatalog.ListItems(archive);
            var instrumentItems = items.Where(item => !item.IsLyrics).ToArray();
            var lyricsItem = items.FirstOrDefault(item => item.IsLyrics);
            var selectedItems = command.ArrangementNumber is { } selectedNumber
                ? [items.ElementAtOrDefault(selectedNumber - 1)
                    ?? throw new ArrangementNotFoundException(selectedNumber)]
                : instrumentItems;
            Song? firstArrangementSong = null;
            if (instrumentItems.Length > 0)
            {
                archive.Position = 0;
                firstArrangementSong = _exportArrangement.Import(archive, instrumentItems[0].Number);
            }

            standardOutput.WriteLine("Archive statistics");
            standardOutput.WriteLine($"Platform: {GetPlatformName(details.ArchivePaths)}");
            standardOutput.WriteLine($"PSARC version: {details.Header.Version}");
            standardOutput.WriteLine($"Archive entries: {details.Header.FileCount}");
            standardOutput.WriteLine($"Encrypted TOC: {(details.Header.ArchiveFlags == 0x4 ? "yes" : "no")}");
            standardOutput.WriteLine();
            standardOutput.WriteLine("Song metadata");
            standardOutput.WriteLine($"Title: {Available(firstArrangementSong?.Title ?? string.Empty)}");
            standardOutput.WriteLine($"Artist: {Available(firstArrangementSong?.Artist ?? string.Empty)}");
            standardOutput.WriteLine($"Album: {Available(firstArrangementSong?.AlbumName ?? string.Empty)}");
            standardOutput.WriteLine($"Year: {Available(firstArrangementSong?.AlbumYear ?? string.Empty)}");
            standardOutput.WriteLine();
            standardOutput.WriteLine("Arrangements");
            foreach (var item in items)
            {
                standardOutput.WriteLine($"{item.Number}. {item.Name}{(item.IsLyrics ? " (lyrics)" : string.Empty)}");
            }

            if (lyricsItem is not null && ReferenceEquals(selectedItems.FirstOrDefault(), lyricsItem))
            {
                standardOutput.WriteLine();
                standardOutput.WriteLine($"{lyricsItem.Number}. Lyrics statistics");
                standardOutput.WriteLine("Notes, chords, sections, techniques, strings, frets, and duration: unavailable for lyrics.");
                return SuccessExitCode;
            }

            foreach (var item in selectedItems)
            {
                if (item.IsLyrics)
                {
                    continue;
                }

                var song = firstArrangementSong is not null && item.Number == instrumentItems[0].Number
                    ? firstArrangementSong
                    : ImportArrangement(archive, item.Number);
                WriteArrangementStatistics(standardOutput, item, song);
            }

            return SuccessExitCode;
        }

        catch (ArrangementNotFoundException exception)
        {
            standardError.WriteLine($"Error: {exception.Message} Run 'psarc2tab --in <input.psarc> --list' to see valid numbers.");
            return InvalidArrangementSelectionExitCode;
        }
        catch (Exception exception) when (IsInputFailure(exception))
        {
            standardError.WriteLine($"Error: Cannot read statistics from '{command.InputPath}': {exception.Message}");
            return InputErrorExitCode;
        }
    }

    /// <summary>Imports one arrangement after rewinding the shared archive stream.</summary>
    /// <param name="input">The readable archive stream.</param>
    /// <param name="arrangementNumber">The one-based arrangement number.</param>
    /// <returns>The imported arrangement song.</returns>
    private Song ImportArrangement(Stream input, int arrangementNumber)
    {
        input.Position = 0;
        return _exportArrangement.Import(input, arrangementNumber);
    }

    /// <summary>Writes counts derived from one successfully imported arrangement.</summary>
    /// <param name="output">The statistics writer.</param>
    /// <param name="item">The selected archive item.</param>
    /// <param name="song">The imported arrangement.</param>
    private static void WriteArrangementStatistics(TextWriter output, RocksmithArchiveItem item, Song song)
    {
        var statistics = ArrangementStatisticsCalculator.Calculate(song);

        output.WriteLine();
        output.WriteLine($"{item.Number}. {item.Name} statistics");
        output.WriteLine($"Notes: {statistics.NoteCount}");
        output.WriteLine($"Chords: {statistics.ChordCount}");
        output.WriteLine($"Sections: {statistics.SectionCount}");
        output.WriteLine($"Bends: {statistics.BendCount}");
        output.WriteLine($"Slides: {statistics.SlideCount}");
        output.WriteLine($"Hammer-ons: {statistics.HammerOnCount}");
        output.WriteLine($"Pull-offs: {statistics.PullOffCount}");
        output.WriteLine($"Used strings: {(statistics.UsedStrings.Count == 0 ? "none" : string.Join(", ", statistics.UsedStrings))}");
        output.WriteLine($"Fret range: {(statistics.MinimumFret is null ? "unavailable" : $"{statistics.MinimumFret}–{statistics.MaximumFret}")}");
        output.WriteLine($"Length: {FormatDuration(statistics.Length)}");
    }

    /// <summary>Formats a timeline duration as hours, minutes, seconds, and milliseconds.</summary>
    /// <param name="duration">The nonnegative duration.</param>
    /// <returns>A fixed-width time value.</returns>
    private static string FormatDuration(TimeSpan duration) =>
        $"{(long)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}.{duration.Milliseconds:000}";

    /// <summary>Displays blank metadata as unavailable.</summary>
    /// <param name="value">The optional metadata value.</param>
    /// <returns>The value or an explicit unavailable marker.</returns>
    private static string Available(string value) => string.IsNullOrWhiteSpace(value) ? "unavailable" : value;

    /// <summary>Infers platform from recognized platform-specific SNG archive paths.</summary>
    /// <param name="paths">The archive paths resolved from its manifest.</param>
    /// <returns>A platform label, or unavailable when the paths do not establish it.</returns>
    private static string GetPlatformName(IReadOnlyList<string> paths)
    {
        var mac = paths.Any(path => path.StartsWith("songs/bin/macos/", StringComparison.OrdinalIgnoreCase));
        var pc = paths.Any(path => path.StartsWith("songs/bin/generic/", StringComparison.OrdinalIgnoreCase));
        return (pc, mac) switch
        {
            (true, false) => "PC",
            (false, true) => "Mac",
            (true, true) => "Mixed",
            _ => "unavailable"
        };
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

        if (string.Equals(command.FormatName, "heatmap", StringComparison.OrdinalIgnoreCase) &&
            command.OutputPath is null)
        {
            return ShowHeatmaps(command, standardOutput, standardError);
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
                if (string.Equals(command.FormatName, "heatmap", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("A fretboard heatmap can only be exported for an instrument arrangement.");
                }

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
                    command.OutputPath!, command.FormatName, command.IncludeDrums);
                standardOutput.WriteLine($"Wrote {command.FormatName} export to '{command.OutputPath}'.");
            }

            return SuccessExitCode;
        }
        catch (InvalidOperationException exception)
        {
            standardError.WriteLine($"Error: Cannot export selected archive item: {exception.Message}");
            return ConversionErrorExitCode;
        }
        catch (InvalidDataException exception)
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

    /// <summary>Renders one or more heatmaps to stdout without creating output files.</summary>
    /// <param name="command">A heatmap request without an output path.</param>
    /// <param name="standardOutput">Writer for the rendered heatmaps.</param>
    /// <param name="standardError">Writer for input or conversion diagnostics.</param>
    /// <returns>The success, input, selection, or conversion error code.</returns>
    private int ShowHeatmaps(ParsedCommand command, TextWriter standardOutput, TextWriter standardError)
    {
        Song[] songs;
        try
        {
            using var archive = File.OpenRead(command.InputPath!);
            var items = _arrangementCatalog.ListItems(archive);
            var selectedItems = command.ArrangementNumber is { } arrangementNumber
                ? [items.ElementAtOrDefault(arrangementNumber - 1)
                    ?? throw new ArrangementNotFoundException(arrangementNumber)]
                : items.Where(item => !item.IsLyrics).ToArray();
            if (selectedItems.Any(item => item.IsLyrics))
            {
                standardError.WriteLine("Error: A fretboard heatmap is available only for an instrument arrangement.");
                return ConversionErrorExitCode;
            }

            if (selectedItems.Length == 0)
            {
                throw new InvalidDataException("The archive contains no instrument arrangements to show.");
            }

            var importedSongs = new List<Song>(selectedItems.Length);
            foreach (var item in selectedItems)
            {
                archive.Position = 0;
                importedSongs.Add(_exportArrangement.Import(archive, item.Number));
            }

            songs = importedSongs.ToArray();
        }
        catch (ArrangementNotFoundException exception)
        {
            standardError.WriteLine(
                $"Error: {exception.Message} Run 'psarc2tab --in <input.psarc> --list' to see valid numbers.");
            return InvalidArrangementSelectionExitCode;
        }
        catch (InvalidOperationException exception)
        {
            standardError.WriteLine($"Error: Cannot import selected arrangement: {exception.Message}");
            return ConversionErrorExitCode;
        }
        catch (Exception exception) when (IsInputFailure(exception))
        {
            standardError.WriteLine($"Error: Cannot read heatmap input from '{command.InputPath}': {exception.Message}");
            return InputErrorExitCode;
        }

        string[] heatmaps;
        try
        {
            heatmaps = songs.Select(song =>
            {
                using var stream = new MemoryStream();
                _exportArrangement.Write(song, "heatmap", stream);
                return Encoding.UTF8.GetString(stream.ToArray());
            }).ToArray();
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException)
        {
            standardError.WriteLine($"Error: Cannot create fretboard heatmap: {exception.Message}");
            return ConversionErrorExitCode;
        }

        var useColors = ShouldUseConsoleColors(standardOutput);
        for (var index = 0; index < heatmaps.Length; index++)
        {
            if (index > 0)
            {
                standardOutput.WriteLine();
            }

            WriteHeatmapText(standardOutput, heatmaps[index], useColors);
        }

        return SuccessExitCode;
    }

    /// <summary>Checks terminal, redirection, and NO_COLOR settings before enabling color.</summary>
    /// <param name="standardOutput">The CLI output writer.</param>
    /// <returns><see langword="true"/> only when stdout is an interactive color-capable console.</returns>
    private static bool ShouldUseConsoleColors(TextWriter standardOutput)
    {
        var noColor = Environment.GetEnvironmentVariable("NO_COLOR");
        var terminal = Environment.GetEnvironmentVariable("TERM");
        return standardOutput is not StringWriter && ReferenceEquals(standardOutput, Console.Out) &&
            !Console.IsOutputRedirected &&
            string.IsNullOrEmpty(noColor) && !string.Equals(terminal, "dumb", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Writes heatmap text and colors shaded cells when an interactive console is available.</summary>
    /// <param name="output">The writer receiving the text.</param>
    /// <param name="text">The plain UTF-8 heatmap decoded as text.</param>
    /// <param name="useColors">Whether the real console can display foreground colors.</param>
    private static void WriteHeatmapText(TextWriter output, string text, bool useColors)
    {
        if (!useColors)
        {
            output.Write(text);
            return;
        }

        var originalColor = Console.ForegroundColor;
        try
        {
            foreach (var character in text)
            {
                var color = character switch
                {
                    '░' => ConsoleColor.DarkCyan,
                    '▒' => ConsoleColor.Cyan,
                    '▓' => ConsoleColor.Yellow,
                    '█' => ConsoleColor.Red,
                    _ => (ConsoleColor?)null
                };
                if (color is { } foreground)
                {
                    Console.ForegroundColor = foreground;
                }

                output.Write(character);
                if (color is not null)
                {
                    Console.ForegroundColor = originalColor;
                }
            }
        }
        finally
        {
            Console.ForegroundColor = originalColor;
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
            outputPaths = _exportArrangement.SupportsMultiTrackFormat(command.FormatName)
                ? [Path.GetFullPath(command.OutputPath!)]
                : CreateArrangementOutputPaths(command.OutputPath!, arrangements);
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
                var outputPath = outputPaths.Length == 1
                    ? outputPaths[0]
                    : outputPaths[index];
                exports.Add((song, outputPath));
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
            WriteOutputSetSafely(exports, command.FormatName, command.IncludeDrums);
            var writtenPaths = _exportArrangement.SupportsMultiTrackFormat(command.FormatName)
                ? [exports[0].OutputPath]
                : exports.Select(export => export.OutputPath).ToArray();
            foreach (var outputPath in writtenPaths)
            {
                standardOutput.WriteLine($"Wrote {command.FormatName} export to '{outputPath}'.");
            }

            return SuccessExitCode;
        }
        catch (InvalidOperationException exception)
        {
            standardError.WriteLine($"Error: Cannot export all arrangements: {exception.Message}");
            return ConversionErrorExitCode;
        }
        catch (InvalidDataException exception)
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
    private void WriteOutputSetSafely(IReadOnlyList<(Song Song, string OutputPath)> exports, string formatName,
        bool includeDrums = false)
    {
        var exporter = includeDrums ? _exportArrangementWithDrums : _exportArrangement;
        var stagedFiles = new List<(string TemporaryPath, string OutputPath)>();
        var committedPaths = new List<string>();
        try
        {
            if (exporter.SupportsMultiTrackFormat(formatName))
            {
                var export = exports[0];
                var outputPath = Path.GetFullPath(export.OutputPath);
                var directory = Path.GetDirectoryName(outputPath)
                    ?? throw new DirectoryNotFoundException("The output directory does not exist.");
                var temporaryPath = Path.Combine(directory, $".psarc2tab-{Guid.NewGuid():N}.tmp");
                using var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                stagedFiles.Add((temporaryPath, outputPath));
                exporter.WriteMultiple(exports.Select(item => item.Song).ToArray(), formatName, stream);
                stream.Flush(flushToDisk: true);
            }
            else
            {
                foreach (var export in exports)
                {
                    var outputPath = Path.GetFullPath(export.OutputPath);
                    var directory = Path.GetDirectoryName(outputPath)
                        ?? throw new DirectoryNotFoundException("The output directory does not exist.");
                    var temporaryPath = Path.Combine(directory, $".psarc2tab-{Guid.NewGuid():N}.tmp");
                    using var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    stagedFiles.Add((temporaryPath, outputPath));
                    exporter.Write(export.Song, formatName, stream);
                    stream.Flush(flushToDisk: true);
                }
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
    private void WriteOutputSafely(Song song, string outputPath, string formatName, bool includeDrums = false)
    {
        var exporter = includeDrums ? _exportArrangementWithDrums : _exportArrangement;
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
                exporter.Write(song, formatName, stream);
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
        psarc2tab - export Rocksmith arrangements as tablature, MIDI, or fretboard heatmaps

        Usage:
          psarc2tab --in <input.psarc> --out <output.tab>
          psarc2tab --in <input.psarc> --out <output.tab> --format tab
          psarc2tab --in <input.psarc> --out <output.mid> --format midi
          psarc2tab --in <input.psarc> --format heatmap [--arr <number|all>]
          psarc2tab --in <input.psarc> --out <output.txt> --format heatmap [--arr <number|all>]
          psarc2tab --in <input.psarc> --out <output.lrc> --arr <lyrics-number>
          psarc2tab --in <input.psarc> --list
          psarc2tab --in <input.psarc> --stats [--arr <number|all>]
          psarc2tab --help
          psarc2tab --version

        Examples:
          psarc2tab --in song.psarc --format heatmap
          psarc2tab --in song.psarc --arr 2 --format heatmap
          psarc2tab --in song.psarc --out song.txt --format heatmap --arr 2
          psarc2tab --in song.psarc --out song.mid --format midi --arr 1
          psarc2tab --in song.psarc --out song.mid --format midi --drums
          psarc2tab --in song.psarc --out song.tab
          psarc2tab --in song.psarc --out song.tab --arr all

        Options:
          --in <path>        Input PSARC archive
          --out <path>       Output file (omit only to show a heatmap on screen)
          --list             List numbered arrangements and lyrics in the archive
          --stats            Show archive and arrangement statistics without writing files
          --arr <n|all>      Select one item or all instrument arrangements (default: all)
          --format <name>    Output format: tab, midi, or heatmap (default: tab)
          --drums            Add a synthetic beat-based drum track (requires --format midi)
          --help             Show this help
          --version          Show version information

        Heatmaps without --out are colored only in an interactive terminal. Set NO_COLOR
        to disable color; redirected output is plain UTF-8 text.

        Exit codes:
          0  Help, version, or conversion completed
          2  Invalid arguments
          3  Input file cannot be read
          4  Output destination cannot be written
          5  Arrangement could not be exported
          6  Arrangement number is not available
        """;
}
