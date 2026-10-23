using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Psarc2Tab.Application;
using Psarc2Tab.Cli;
using Psarc2Tab.Domain;
using Psarc2Tab.Formats.Rocksmith;
using Psarc2Tab.Tablature;

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
        Assert.Contains("--in <path>", output.StandardOutput);
        Assert.Contains("--version", output.StandardOutput);
        Assert.Contains("--list", output.StandardOutput);
        Assert.Contains("--arr <n|all>", output.StandardOutput);
        Assert.Contains("--arr all", output.StandardOutput);
        Assert.Contains("default: all", output.StandardOutput);
        Assert.Contains("--format <name>", output.StandardOutput);
        Assert.Contains("--in song.psarc --format heatmap", output.StandardOutput);
        Assert.Contains("--arr 2 --format heatmap", output.StandardOutput);
        Assert.Contains("NO_COLOR", output.StandardOutput);
        Assert.DoesNotContain("beta/experimental", output.StandardOutput);
        Assert.DoesNotContain("--arrangement <", output.StandardOutput);
        Assert.DoesNotContain("\n  -in ", output.StandardOutput);
        Assert.DoesNotContain("\n  -out ", output.StandardOutput);
        Assert.Empty(output.StandardError);
    }

    /// <summary>Rejects a format that has no registered writer and reports available formats.</summary>
    [Fact]
    public void Run_UnsupportedFormat_ReturnsInvalidArguments()
    {
        var output = Run("--in", "song.psarc", "--out", "song.xyz", "--format", "unknown");

        Assert.Equal(CliApplication.InvalidArgumentsExitCode, output.ExitCode);
        Assert.Contains("Unsupported output format 'unknown'", output.StandardError);
        Assert.Contains("Supported formats: heatmap, midi, tab", output.StandardError);
        Assert.Empty(output.StandardOutput);
    }

    /// <summary>Exports a selected archive arrangement as a Standard MIDI File.</summary>
    [Fact]
    public void Run_MidiFormat_WritesStandardMidiFile()
    {
        using var directory = new TemporaryDirectory();
        var inputPath = Path.Combine(directory.Path, "song.psarc");
        var outputPath = Path.Combine(directory.Path, "song.mid");
        File.WriteAllBytes(inputPath, CreateArchive(ArrangementXml));

        var output = Run("--in", inputPath, "--out", outputPath, "--format", "midi");

        Assert.Equal(CliApplication.SuccessExitCode, output.ExitCode);
        Assert.Equal("MThd", Encoding.ASCII.GetString(File.ReadAllBytes(outputPath), 0, 4));
        Assert.Contains("Wrote midi export", output.StandardOutput);
        Assert.Empty(output.StandardError);
    }

    /// <summary>Exports the selected arrangement as a beta shaded-text fretboard heatmap.</summary>
    [Fact]
    public void Run_HeatmapFormat_WritesTextHeatmap()
    {
        using var directory = new TemporaryDirectory();
        var inputPath = Path.Combine(directory.Path, "song.psarc");
        var outputPath = Path.Combine(directory.Path, "song.txt");
        File.WriteAllBytes(inputPath, CreateArchive(ArrangementXml));

        var output = Run("--in", inputPath, "--out", outputPath, "--format", "heatmap", "--arr", "1");

        Assert.Equal(CliApplication.SuccessExitCode, output.ExitCode);
        Assert.Contains("Wrote heatmap export", output.StandardOutput);
        Assert.Empty(output.StandardError);
        var heatmap = File.ReadAllText(outputPath);
        Assert.Contains("Fretboard heatmap (beta)", heatmap);
        Assert.Contains("Arrangement: Lead", heatmap);
        Assert.Contains("Maximum uses at one position: 1", heatmap);
    }

    /// <summary>Displays the selected heatmap on stdout without creating a file or adding color escapes.</summary>
    [Fact]
    public void Run_HeatmapWithoutOutputPath_DisplaysPlainText()
    {
        using var directory = new TemporaryDirectory();
        var inputPath = Path.Combine(directory.Path, "song.psarc");
        File.WriteAllBytes(inputPath, CreateArchive(ArrangementXml));

        var output = Run("--in", inputPath, "--arr", "1", "--format", "heatmap");

        Assert.Equal(CliApplication.SuccessExitCode, output.ExitCode);
        Assert.Contains("Fretboard heatmap (beta)", output.StandardOutput);
        Assert.Contains("Arrangement: Lead", output.StandardOutput);
        Assert.Empty(output.StandardError);
        Assert.Single(Directory.GetFiles(directory.Path));
    }

    /// <summary>Displays all selected instrument heatmaps on stdout and omits Lyrics.</summary>
    [Fact]
    public void Run_HeatmapWithoutOutputPathAndDefaultSelection_DisplaysEachArrangement()
    {
        using var directory = new TemporaryDirectory();
        var inputPath = Path.Combine(directory.Path, "song.psarc");
        File.WriteAllBytes(inputPath, CreateArchive(
            ("songs/arr/sample_lead.xml", ArrangementXml),
            ("songs/arr/sample_bass.xml", BassArrangementXml),
            ("songs/arr/sample_vocals.xml", "<vocals />")));

        var output = Run("--in", inputPath, "--format", "heatmap");

        Assert.Equal(CliApplication.SuccessExitCode, output.ExitCode);
        Assert.Contains("Arrangement: Lead", output.StandardOutput);
        Assert.Contains("Arrangement: Bass", output.StandardOutput);
        Assert.DoesNotContain("Arrangement: Lyrics", output.StandardOutput);
        Assert.Equal(2, output.StandardOutput.Split("Fretboard heatmap (beta)", StringSplitOptions.None).Length - 1);
        Assert.Empty(output.StandardError);
        Assert.Single(Directory.GetFiles(directory.Path));
    }

    /// <summary>Writes one safely named heatmap for each selected instrument arrangement.</summary>
    [Fact]
    public void Run_AllArrangements_HeatmapWritesSeparateTextFiles()
    {
        using var directory = new TemporaryDirectory();
        var inputPath = Path.Combine(directory.Path, "song.psarc");
        var outputPath = Path.Combine(directory.Path, "mysong.txt");
        File.WriteAllBytes(inputPath, CreateArchive(
            ("songs/arr/sample_lead.xml", ArrangementXml),
            ("songs/arr/sample_bass.xml", BassArrangementXml),
            ("songs/arr/sample_vocals.xml", "<vocals />")));

        var output = Run("--in", inputPath, "--out", outputPath, "--format", "heatmap");

        Assert.Equal(CliApplication.SuccessExitCode, output.ExitCode);
        Assert.True(File.Exists(Path.Combine(directory.Path, "mysong-lead.txt")));
        Assert.True(File.Exists(Path.Combine(directory.Path, "mysong-bass.txt")));
        Assert.False(File.Exists(outputPath));
        Assert.Equal(2, Directory.GetFiles(directory.Path, "mysong-*.txt").Length);
        Assert.Contains("Tuning (low to high): E2 A2 D3 G3 B3 E4",
            File.ReadAllText(Path.Combine(directory.Path, "mysong-lead.txt")));
        Assert.Contains("Tuning (low to high): E1 A1 D2 G2 B2 E3",
            File.ReadAllText(Path.Combine(directory.Path, "mysong-bass.txt")));
        Assert.Contains("mysong-lead.txt", output.StandardOutput);
        Assert.Contains("mysong-bass.txt", output.StandardOutput);
        Assert.Empty(Directory.GetFiles(directory.Path, ".psarc2tab-*.tmp"));
        Assert.Empty(output.StandardError);
    }

    /// <summary>Reports a chart fret beyond the ASCII display limit as an export error.</summary>
    [Fact]
    public void Run_HeatmapWithFretAboveLimit_ReturnsConversionErrorWithoutOutput()
    {
        using var directory = new TemporaryDirectory();
        var inputPath = Path.Combine(directory.Path, "song.psarc");
        var outputPath = Path.Combine(directory.Path, "song.txt");
        File.WriteAllBytes(inputPath, CreateArchive(ArrangementXml.Replace("fret=\"3\"", "fret=\"38\"",
            StringComparison.Ordinal)));

        var output = Run("--in", inputPath, "--out", outputPath, "--format", "heatmap", "--arr", "1");

        Assert.Equal(CliApplication.ConversionErrorExitCode, output.ExitCode);
        Assert.Contains("exceeds the text heatmap limit", output.StandardError);
        Assert.False(File.Exists(outputPath));
        Assert.Empty(Directory.GetFiles(directory.Path, ".psarc2tab-*.tmp"));
    }

    /// <summary>Removes staged heatmaps if a later arrangement exceeds the supported fret range.</summary>
    [Fact]
    public void Run_AllArrangements_HeatmapFailureRollsBackStagedFiles()
    {
        using var directory = new TemporaryDirectory();
        var inputPath = Path.Combine(directory.Path, "song.psarc");
        var outputPath = Path.Combine(directory.Path, "mysong.txt");
        var invalidBass = BassArrangementXml.Replace("fret=\"3\"", "fret=\"38\"", StringComparison.Ordinal);
        File.WriteAllBytes(inputPath, CreateArchive(
            ("songs/arr/sample_lead.xml", ArrangementXml),
            ("songs/arr/sample_bass.xml", invalidBass)));

        var output = Run("--in", inputPath, "--out", outputPath, "--format", "heatmap");

        Assert.Equal(CliApplication.ConversionErrorExitCode, output.ExitCode);
        Assert.Contains("exceeds the text heatmap limit", output.StandardError);
        Assert.Empty(Directory.GetFiles(directory.Path, "mysong-*.txt"));
        Assert.Empty(Directory.GetFiles(directory.Path, ".psarc2tab-*.tmp"));
    }

    /// <summary>Rejects Lyrics as a heatmap target without creating an output file.</summary>
    [Fact]
    public void Run_HeatmapWithLyricsSelection_ReturnsConversionError()
    {
        using var directory = new TemporaryDirectory();
        var inputPath = Path.Combine(directory.Path, "song.psarc");
        var outputPath = Path.Combine(directory.Path, "lyrics.txt");
        File.WriteAllBytes(inputPath, CreateArchive(
            ("songs/arr/sample_lead.xml", ArrangementXml),
            ("songs/arr/sample_vocals.xml", "<vocals />")));

        var output = Run("--in", inputPath, "--out", outputPath, "--format", "heatmap", "--arr", "2");

        Assert.Equal(CliApplication.ConversionErrorExitCode, output.ExitCode);
        Assert.Contains("only be exported for an instrument arrangement", output.StandardError);
        Assert.False(File.Exists(outputPath));
    }

    /// <summary>Prints archive and imported arrangement statistics without creating an export file.</summary>
    [Fact]
    public void Run_Statistics_PrintsCountsAndMissingMetadataWithoutWritingFiles()
    {
        using var directory = new TemporaryDirectory();
        var inputPath = Path.Combine(directory.Path, "song.psarc");
        File.WriteAllBytes(inputPath, CreateArchive(StatisticsArrangementXml));

        var output = Run("--in", inputPath, "--stats");

        Assert.Equal(CliApplication.SuccessExitCode, output.ExitCode);
        Assert.Contains("PSARC version: 1.4", output.StandardOutput);
        Assert.Contains("Platform: unavailable", output.StandardOutput);
        Assert.Contains("Archive entries: 2", output.StandardOutput);
        Assert.Contains("Encrypted TOC: no", output.StandardOutput);
        Assert.Contains("Title: Test Stats", output.StandardOutput);
        Assert.Contains("Artist: Test Artist", output.StandardOutput);
        Assert.Contains("Album: Test Album", output.StandardOutput);
        Assert.Contains("Year: 1998", output.StandardOutput);
        Assert.Contains("Notes: 1", output.StandardOutput);
        Assert.Contains("Bends: 1", output.StandardOutput);
        Assert.Contains("Slides: 1", output.StandardOutput);
        Assert.Contains("Hammer-ons: 1", output.StandardOutput);
        Assert.Contains("Pull-offs: 1", output.StandardOutput);
        Assert.Contains("Used strings: 2", output.StandardOutput);
        Assert.Contains("Fret range: 5–5", output.StandardOutput);
        Assert.Contains("Length: 00:01:01.500", output.StandardOutput);
        Assert.Empty(output.StandardError);
        Assert.Single(Directory.GetFiles(directory.Path));
    }

    /// <summary>Limits arrangement statistics to the requested numbered item.</summary>
    [Fact]
    public void Run_StatisticsWithArrangement_ImportsOnlySelectedArrangement()
    {
        using var directory = new TemporaryDirectory();
        var inputPath = Path.Combine(directory.Path, "song.psarc");
        File.WriteAllBytes(inputPath, CreateArchive(
            ("songs/arr/sample_lead.xml", ArrangementXml),
            ("songs/arr/sample_bass.xml", BassArrangementXml)));

        var output = Run("--in", inputPath, "--stats", "--arr", "2");

        Assert.Equal(CliApplication.SuccessExitCode, output.ExitCode);
        Assert.Contains("2. Bass statistics", output.StandardOutput);
        Assert.DoesNotContain("1. Lead statistics", output.StandardOutput);
        Assert.Contains("1. Lead", output.StandardOutput);
        Assert.Contains("Title: Test Song", output.StandardOutput);
        Assert.DoesNotContain("Title: Test Bass", output.StandardOutput);
        Assert.DoesNotContain("Artist: Test Artist", output.StandardOutput.Split("Arrangements", StringSplitOptions.None).Last());
        Assert.Empty(output.StandardError);
    }

    /// <summary>Shows a clear unavailable-metrics message for a selected Lyrics item.</summary>
    [Fact]
    public void Run_StatisticsWithLyricsSelection_ReportsUnavailableInstrumentMetrics()
    {
        using var directory = new TemporaryDirectory();
        var inputPath = Path.Combine(directory.Path, "song.psarc");
        File.WriteAllBytes(inputPath, CreateArchive(
            ("songs/arr/sample_lead.xml", ArrangementXml),
            ("songs/arr/sample_vocals.xml", "<vocals />")));

        var output = Run("--in", inputPath, "--stats", "--arr", "2");

        Assert.Equal(CliApplication.SuccessExitCode, output.ExitCode);
        Assert.Contains("2. Lyrics statistics", output.StandardOutput);
        Assert.Contains("unavailable for lyrics", output.StandardOutput);
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
        Assert.Contains("'--out' is required", output.StandardError);
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

        var output = Run("--in", inputPath, "--out", outputPath, "--arr", "1");

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

        var output = Run("--in", inputPath, "--out", directory.Path, "--arr", "1");

        Assert.Equal(CliApplication.OutputErrorExitCode, output.ExitCode);
        Assert.Contains("destination path is a directory", output.StandardError);
    }

    /// <summary>Rejects an output path whose parent directory does not exist.</summary>
    [Fact]
    public void Run_OutputDirectoryMissing_ReturnsOutputError()
    {
        using var directory = new TemporaryDirectory();
        var inputPath = Path.Combine(directory.Path, "song.psarc");
        File.WriteAllBytes(inputPath, CreateArchive(ArrangementXml));
        var outputPath = Path.Combine(directory.Path, "missing", "song.tab");

        var output = Run("--in", inputPath, "--out", outputPath);

        Assert.Equal(CliApplication.OutputErrorExitCode, output.ExitCode);
        Assert.Contains("destination directory does not exist", output.StandardError);
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tab"));
    }

    /// <summary>Imports and writes a selected arrangement from a PSARC archive.</summary>
    [Fact]
    public void Run_ValidArchive_WritesTabAndReturnsSuccess()
    {
        using var directory = new TemporaryDirectory();
        var inputPath = Path.Combine(directory.Path, "song.psarc");
        var outputPath = Path.Combine(directory.Path, "song.tab");
        File.WriteAllBytes(inputPath, CreateArchive(
            ("songs/arr/sample_lead.xml", ArrangementXml),
            ("songs/arr/sample_bass.xml", BassArrangementXml)));

        var output = Run("--in", inputPath, "--out", outputPath, "--arr", "2");

        Assert.Equal(CliApplication.SuccessExitCode, output.ExitCode);
        Assert.Contains("Wrote tab export", output.StandardOutput);
        Assert.Empty(output.StandardError);
        var tab = File.ReadAllText(outputPath);
        Assert.Equal(string.Join('\n',
        [
            "Artist: Test Artist",
            "Song: Test Bass",
            "Tuning: E1 A1 D2 G2",
            "Tuning reference: A4=440.00 Hz (0 cents)",
            string.Empty,
            "g|-|",
            "D|-|",
            "A|-|",
            "E|3|",
            string.Empty
        ]), tab);
        Assert.Empty(Directory.GetFiles(directory.Path, ".psarc2tab-*.tmp"));
    }

    /// <summary>Exports all instrument arrangements and excludes the lyrics listing item.</summary>
    [Fact]
    public void Run_DefaultArrangementSelection_WritesOneSuffixedFilePerInstrument()
    {
        using var directory = new TemporaryDirectory();
        var inputPath = Path.Combine(directory.Path, "song.psarc");
        var outputPath = Path.Combine(directory.Path, "mysong.tab");
        File.WriteAllBytes(inputPath, CreateArchive(
            ("songs/arr/sample_lead.xml", ArrangementXml),
            ("songs/arr/sample_bass.xml", BassArrangementXml),
            ("songs/arr/sample_rhythm.xml", RhythmArrangementXml),
            ("songs/arr/sample_vocals.xml", "<vocals><vocal time=\"1\" note=\"60\" length=\"1\" lyric=\"Hi\" /></vocals>")));

        var output = Run("--in", inputPath, "--out", outputPath);

        Assert.Equal(CliApplication.SuccessExitCode, output.ExitCode);
        Assert.Empty(output.StandardError);
        Assert.Contains("mysong-lead.tab", output.StandardOutput);
        Assert.Contains("mysong-bass.tab", output.StandardOutput);
        Assert.Contains("mysong-rhythm.tab", output.StandardOutput);
        Assert.Equal(3, Directory.GetFiles(directory.Path, "mysong-*.tab").Length);
        Assert.Contains("Song: Test Song", File.ReadAllText(Path.Combine(directory.Path, "mysong-lead.tab")));
        Assert.Contains("Song: Test Bass", File.ReadAllText(Path.Combine(directory.Path, "mysong-bass.tab")));
        Assert.Contains("Song: Test Rhythm", File.ReadAllText(Path.Combine(directory.Path, "mysong-rhythm.tab")));
        Assert.Empty(Directory.GetFiles(directory.Path, ".psarc2tab-*.tmp"));
    }

    /// <summary>Writes all MIDI arrangements to one type-one file in archive order.</summary>
    [Fact]
    public void Run_AllArrangements_MidiWritesOneMultiTrackFile()
    {
        using var directory = new TemporaryDirectory();
        var inputPath = Path.Combine(directory.Path, "song.psarc");
        var outputPath = Path.Combine(directory.Path, "mysong.mid");
        File.WriteAllBytes(inputPath, CreateArchive(
            ("songs/arr/sample_rhythm.xml", RhythmArrangementXml),
            ("songs/arr/sample_lead.xml", ArrangementXml),
            ("songs/arr/sample_bass.xml", BassArrangementXml),
            ("songs/arr/sample_vocals.xml", "<vocals><vocal time=\"1\" note=\"60\" length=\"1\" lyric=\"Hi\" /></vocals>")));

        var output = Run("--in", inputPath, "--out", outputPath, "--format", "midi", "--arr", "all");

        var midi = File.ReadAllBytes(outputPath);
        var text = Encoding.UTF8.GetString(midi);
        Assert.Equal(CliApplication.SuccessExitCode, output.ExitCode);
        Assert.Empty(output.StandardError);
        Assert.Contains("Wrote midi export to", output.StandardOutput);
        Assert.Equal((ushort)1, System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(midi.AsSpan(8, 2)));
        Assert.Equal((ushort)4, System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(midi.AsSpan(10, 2)));
        var conductorPosition = text.IndexOf("psarc2tab tempo map", StringComparison.Ordinal);
        var rhythmPosition = text.IndexOf("psarc2tab Rhythm", StringComparison.Ordinal);
        var leadPosition = text.IndexOf("psarc2tab Lead", StringComparison.Ordinal);
        var bassPosition = text.IndexOf("psarc2tab Bass", StringComparison.Ordinal);
        Assert.True(conductorPosition >= 0 && conductorPosition < rhythmPosition &&
            rhythmPosition < leadPosition && leadPosition < bassPosition);
        Assert.Single(Directory.GetFiles(directory.Path, "mysong*.mid"));
        Assert.Empty(Directory.GetFiles(directory.Path, ".psarc2tab-*.tmp"));
    }

    /// <summary>Rejects mismatched beat maps without leaving a partial combined MIDI file.</summary>
    [Fact]
    public void Run_AllArrangements_MidiWithDifferentBeatMapsLeavesNoOutput()
    {
        using var directory = new TemporaryDirectory();
        var inputPath = Path.Combine(directory.Path, "song.psarc");
        var outputPath = Path.Combine(directory.Path, "mysong.mid");
        var leadXml = AddBeatTimeline(ArrangementXml, "0.5");
        var bassXml = AddBeatTimeline(BassArrangementXml, "0.6");
        File.WriteAllBytes(inputPath, CreateArchive(
            ("songs/arr/sample_lead.xml", leadXml),
            ("songs/arr/sample_bass.xml", bassXml)));

        var output = Run("--in", inputPath, "--out", outputPath, "--format", "midi", "--arr", "all");

        Assert.Equal(CliApplication.ConversionErrorExitCode, output.ExitCode);
        Assert.Contains("requires identical beat times", output.StandardError);
        Assert.False(File.Exists(outputPath));
        Assert.Empty(Directory.GetFiles(directory.Path, ".psarc2tab-*.tmp"));
    }

    /// <summary>Preserves an existing combined MIDI destination on collision.</summary>
    [Fact]
    public void Run_AllArrangements_MidiExistingDestinationIsNotOverwritten()
    {
        using var directory = new TemporaryDirectory();
        var inputPath = Path.Combine(directory.Path, "song.psarc");
        var outputPath = Path.Combine(directory.Path, "mysong.mid");
        File.WriteAllBytes(inputPath, CreateArchive(
            ("songs/arr/sample_lead.xml", ArrangementXml),
            ("songs/arr/sample_bass.xml", BassArrangementXml)));
        File.WriteAllText(outputPath, "keep me");

        var output = Run("--in", inputPath, "--out", outputPath, "--format", "midi", "--arr", "all");

        Assert.Equal(CliApplication.OutputErrorExitCode, output.ExitCode);
        Assert.Equal("keep me", File.ReadAllText(outputPath));
        Assert.Empty(Directory.GetFiles(directory.Path, ".psarc2tab-*.tmp"));
    }

    /// <summary>Adds stable numeric suffixes to repeated instrument arrangement names.</summary>
    [Fact]
    public void Run_AllArrangements_RepeatedInstrumentNamesGetNumberedSuffixes()
    {
        using var directory = new TemporaryDirectory();
        var inputPath = Path.Combine(directory.Path, "song.psarc");
        var outputPath = Path.Combine(directory.Path, "mix.tab");
        File.WriteAllBytes(inputPath, CreateArchive(
            ("songs/arr/sample_lead.xml", ArrangementXml),
            ("songs/arr/alternate_lead.xml", ArrangementXml),
            ("songs/arr/sample_bass.xml", BassArrangementXml)));

        var output = Run("--in", inputPath, "--out", outputPath, "--arr", "all");

        Assert.Equal(CliApplication.SuccessExitCode, output.ExitCode);
        Assert.True(File.Exists(Path.Combine(directory.Path, "mix-lead.tab")));
        Assert.True(File.Exists(Path.Combine(directory.Path, "mix-lead-2.tab")));
        Assert.True(File.Exists(Path.Combine(directory.Path, "mix-bass.tab")));
    }

    /// <summary>Prevalidates every destination and leaves existing files untouched on collision.</summary>
    [Fact]
    public void Run_AllArrangements_ExistingDestinationPreventsWholeExport()
    {
        using var directory = new TemporaryDirectory();
        var inputPath = Path.Combine(directory.Path, "song.psarc");
        File.WriteAllBytes(inputPath, CreateArchive(
            ("songs/arr/sample_lead.xml", ArrangementXml),
            ("songs/arr/sample_bass.xml", BassArrangementXml)));
        var occupiedPath = Path.Combine(directory.Path, "mix-bass.tab");
        File.WriteAllText(occupiedPath, "keep me");

        var output = Run("--in", inputPath, "--out", Path.Combine(directory.Path, "mix.tab"), "--arr", "all");

        Assert.Equal(CliApplication.OutputErrorExitCode, output.ExitCode);
        Assert.Equal("keep me", File.ReadAllText(occupiedPath));
        Assert.False(File.Exists(Path.Combine(directory.Path, "mix-lead.tab")));
        Assert.Empty(Directory.GetFiles(directory.Path, ".psarc2tab-*.tmp"));
    }

    /// <summary>Does not leave any output when an arrangement fails to import.</summary>
    [Fact]
    public void Run_AllArrangements_MalformedArrangementLeavesNoOutputSet()
    {
        using var directory = new TemporaryDirectory();
        var inputPath = Path.Combine(directory.Path, "song.psarc");
        File.WriteAllBytes(inputPath, CreateArchive(
            ("songs/arr/sample_lead.xml", ArrangementXml),
            ("songs/arr/sample_bass.xml", "<song>")));

        var output = Run("--in", inputPath, "--out", Path.Combine(directory.Path, "mix.tab"), "--arr", "all");

        Assert.Equal(CliApplication.InputErrorExitCode, output.ExitCode);
        Assert.Empty(Directory.GetFiles(directory.Path, "mix-*.tab"));
        Assert.Empty(Directory.GetFiles(directory.Path, ".psarc2tab-*.tmp"));
    }

    /// <summary>Rolls back earlier staged files when a writer fails partway through the set.</summary>
    [Fact]
    public void Run_AllArrangements_WriteFailureRemovesStagedFiles()
    {
        using var directory = new TemporaryDirectory();
        var inputPath = Path.Combine(directory.Path, "song.psarc");
        File.WriteAllBytes(inputPath, CreateArchive(
            ("songs/arr/sample_lead.xml", ArrangementXml),
            ("songs/arr/sample_bass.xml", BassArrangementXml)));
        var catalog = new RocksmithArrangementCatalog();
        var importer = new RocksmithArchiveArrangementImporter(catalog, new RocksmithArrangementImporter());
        var exportUseCase = new ExportArrangementUseCase(importer,
            new ArrangementFormatRegistry([new FailingArrangementWriter()]));
        using var standardOutput = new StringWriter();
        using var standardError = new StringWriter();

        var exitCode = new CliApplication(exportArrangement: exportUseCase).Run(
            ["--in", inputPath, "--out", Path.Combine(directory.Path, "mix.tab"), "--arr", "all"],
            standardOutput, standardError);

        Assert.Equal(CliApplication.OutputErrorExitCode, exitCode);
        Assert.Empty(Directory.GetFiles(directory.Path, "mix-*.tab"));
        Assert.Empty(Directory.GetFiles(directory.Path, ".psarc2tab-*.tmp"));
    }

    /// <summary>Exports the final listed archive item as a lyrics-only LRC file.</summary>
    [Fact]
    public void Run_SelectedLyrics_WritesLrcWithoutTablature()
    {
        using var directory = new TemporaryDirectory();
        var inputPath = Path.Combine(directory.Path, "song.psarc");
        var outputPath = Path.Combine(directory.Path, "song.lrc");
        File.WriteAllBytes(inputPath, CreateArchive(
            ("songs/arr/sample_lead.xml", ArrangementXml),
            ("songs/arr/sample_vocals.xml", "<vocals><vocal time=\"1.25\" note=\"60\" length=\"0.2\" lyric=\"Hello+\" /></vocals>")));

        var listing = Run("--in", inputPath, "--list");
        var output = Run("--in", inputPath, "--out", outputPath, "--arr", "2");

        Assert.Equal(CliApplication.SuccessExitCode, listing.ExitCode);
        Assert.Contains("2. Lyrics — songs/arr/sample_vocals.xml", listing.StandardOutput);
        Assert.Equal(CliApplication.SuccessExitCode, output.ExitCode);
        Assert.Contains("Wrote lyrics", output.StandardOutput);
        Assert.Empty(output.StandardError);
        Assert.Equal("[00:01.25]Hello\n", File.ReadAllText(outputPath));
        Assert.Empty(Directory.GetFiles(directory.Path, ".psarc2tab-*.tmp"));
    }

    /// <summary>Reports an unavailable arrangement and creates no output file.</summary>
    [Fact]
    public void Run_UnknownArrangement_ReturnsSelectionError()
    {
        using var directory = new TemporaryDirectory();
        var inputPath = Path.Combine(directory.Path, "song.psarc");
        var outputPath = Path.Combine(directory.Path, "song.tab");
        File.WriteAllBytes(inputPath, CreateArchive(ArrangementXml));

        var output = Run("--in", inputPath, "--out", outputPath, "--arr", "2");

        Assert.Equal(CliApplication.InvalidArrangementSelectionExitCode, output.ExitCode);
        Assert.Contains("Arrangement 2 is not available", output.StandardError);
        Assert.False(File.Exists(outputPath));
    }

    /// <summary>Removes temporary output when an arrangement document is malformed.</summary>
    [Fact]
    public void Run_MalformedArrangement_DoesNotLeaveOutputOrTemporaryFile()
    {
        using var directory = new TemporaryDirectory();
        var inputPath = Path.Combine(directory.Path, "song.psarc");
        var outputPath = Path.Combine(directory.Path, "song.tab");
        File.WriteAllBytes(inputPath, CreateArchive("<song>"));

        var output = Run("--in", inputPath, "--out", outputPath);

        Assert.Equal(CliApplication.InputErrorExitCode, output.ExitCode);
        Assert.Contains("import arrangements from", output.StandardError);
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

    /// <summary>Adds a two-beat timing map to an arrangement fixture.</summary>
    /// <param name="xml">The arrangement XML document.</param>
    /// <param name="secondBeatTime">The time of the second beat.</param>
    /// <returns>The arrangement with beat markers preceding its levels.</returns>
    private static string AddBeatTimeline(string xml, string secondBeatTime) =>
        xml.Replace("<levels>",
            $"<beats><beat time=\"0\" measure=\"0\" beat=\"0\" /><beat time=\"{secondBeatTime}\" measure=\"0\" beat=\"1\" /></beats><levels>",
            StringComparison.Ordinal);

    /// <summary>Builds a minimal PSARC archive with one arrangement XML entry.</summary>
    /// <param name="arrangementXml">The XML payload stored in the archive.</param>
    /// <returns>A valid raw-block PSARC archive.</returns>
    private static byte[] CreateArchive(string arrangementXml)
        => CreateArchive(("songs/arr/sample_lead.xml", arrangementXml));

    /// <summary>Builds a minimal PSARC archive with arrangement XML entries.</summary>
    /// <param name="arrangements">Archive paths and corresponding XML documents.</param>
    /// <returns>A valid raw-block PSARC archive.</returns>
    private static byte[] CreateArchive(params (string Path, string Xml)[] arrangements)
    {
        const int headerSize = 32;
        const int entrySize = 30;
        var manifest = Encoding.UTF8.GetBytes(string.Join('\n', arrangements.Select(item => item.Path)));
        var contents = new[] { manifest }.Concat(arrangements.Select(item => Encoding.UTF8.GetBytes(item.Xml))).ToArray();
        var tocSize = headerSize + contents.Length * (entrySize + sizeof(ushort));
        var archive = new byte[tocSize + contents.Sum(content => content.Length)];

        "PSAR"u8.CopyTo(archive);
        BinaryPrimitives.WriteUInt32BigEndian(archive.AsSpan(4), 0x0001_0004);
        "zlib"u8.CopyTo(archive.AsSpan(8));
        BinaryPrimitives.WriteUInt32BigEndian(archive.AsSpan(12), (uint)tocSize);
        BinaryPrimitives.WriteUInt32BigEndian(archive.AsSpan(16), entrySize);
        BinaryPrimitives.WriteUInt32BigEndian(archive.AsSpan(20), (uint)contents.Length);
        BinaryPrimitives.WriteUInt32BigEndian(archive.AsSpan(24), 65_536);

        var blockTableOffset = headerSize + contents.Length * entrySize;
        var dataOffset = tocSize;
        for (var entryIndex = 0; entryIndex < contents.Length; entryIndex++)
        {
            var recordOffset = headerSize + entryIndex * entrySize;
            if (entryIndex > 0)
            {
                MD5.HashData(Encoding.UTF8.GetBytes(arrangements[entryIndex - 1].Path))
                    .CopyTo(archive.AsSpan(recordOffset, 16));
            }

            BinaryPrimitives.WriteUInt32BigEndian(archive.AsSpan(recordOffset + 16), (uint)entryIndex);
            WriteUInt40(archive.AsSpan(recordOffset + 20), (ulong)contents[entryIndex].Length);
            WriteUInt40(archive.AsSpan(recordOffset + 25), (ulong)dataOffset);
            contents[entryIndex].CopyTo(archive, dataOffset);
            dataOffset += contents[entryIndex].Length;
            BinaryPrimitives.WriteUInt16BigEndian(archive.AsSpan(blockTableOffset + entryIndex * 2), 0);
        }

        return archive;
    }

    /// <summary>Writes an unsigned 40-bit integer in network byte order.</summary>
    /// <param name="destination">The five-byte destination field.</param>
    /// <param name="value">The value to encode.</param>
    private static void WriteUInt40(Span<byte> destination, ulong value)
    {
        for (var index = 4; index >= 0; index--)
        {
            destination[index] = (byte)value;
            value >>= 8;
        }
    }

    /// <summary>A minimal arrangement used by end-to-end CLI tests.</summary>
    private const string ArrangementXml = """
        <song version="7">
          <title>Test Song</title><arrangement>Lead</arrangement><artistName>Test Artist</artistName><offset>0</offset>
          <tuning string0="0" string1="0" string2="0" string3="0" string4="0" string5="0" />
          <chordTemplates />
          <levels><level difficulty="0"><notes><note time="1.25" sustain="0.5" string="0" fret="3" /></notes><chords /></level></levels>
        </song>
        """;

    /// <summary>A chart fixture with measurable events, techniques, and section timing.</summary>
    private const string StatisticsArrangementXml = """
        <song version="7">
          <title>Test Stats</title><arrangement>Lead</arrangement><artistName>Test Artist</artistName>
          <albumName>Test Album</albumName><albumYear>1998</albumYear><offset>0</offset>
          <tuning string0="0" string1="0" string2="0" string3="0" string4="0" string5="0" />
          <sections><section name="verse" number="1" startTime="0" /></sections>
          <chordTemplates />
          <levels><level difficulty="0"><notes>
            <note time="61.5" string="1" fret="5" bend="1" slideTo="7" hammerOn="1" pullOff="1" />
          </notes><chords /></level></levels>
        </song>
        """;

    /// <summary>A minimal bass arrangement used to verify archive-entry selection.</summary>
    private const string BassArrangementXml = """
        <song version="7">
          <title>Test Bass</title><arrangement>Bass</arrangement><artistName>Test Artist</artistName><offset>0</offset>
          <tuning string0="0" string1="0" string2="0" string3="0" string4="0" string5="0" />
          <chordTemplates />
          <levels><level difficulty="0"><notes><note time="1.25" sustain="0.5" string="0" fret="3" /></notes><chords /></level></levels>
        </song>
        """;

    /// <summary>A minimal rhythm arrangement used for all-arrangement export tests.</summary>
    private const string RhythmArrangementXml = """
        <song version="7">
          <title>Test Rhythm</title><arrangement>Rhythm</arrangement><artistName>Test Artist</artistName><offset>0</offset>
          <tuning string0="0" string1="0" string2="0" string3="0" string4="0" string5="0" />
          <chordTemplates />
          <levels><level difficulty="0"><notes><note time="1.25" sustain="0.5" string="0" fret="3" /></notes><chords /></level></levels>
        </song>
        """;

    /// <summary>Fails on the second write to verify rollback of a staged export set.</summary>
    private sealed class FailingArrangementWriter : IArrangementFormatWriter
    {
        /// <summary>The number of writes already attempted.</summary>
        private int _writeCount;

        /// <summary>Gets the registered test format name.</summary>
        public string FormatName => "tab";

        /// <summary>Writes the first arrangement and fails on the next one.</summary>
        /// <param name="song">The song being exported.</param>
        /// <param name="track">The selected track.</param>
        /// <param name="destination">The staged destination stream.</param>
        public void Write(Song song, Track track, Stream destination)
        {
            if (_writeCount++ > 0)
            {
                throw new IOException("Synthetic writer failure.");
            }

            new TablatureTextWriter().Write(song, track, destination);
        }
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
