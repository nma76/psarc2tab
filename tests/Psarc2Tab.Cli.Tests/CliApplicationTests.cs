using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
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

        var output = Run("--in", inputPath, "--out", outputPath, "--arrangement", "2");

        Assert.Equal(CliApplication.SuccessExitCode, output.ExitCode);
        Assert.Contains("Wrote tablature", output.StandardOutput);
        Assert.Empty(output.StandardError);
        var tab = File.ReadAllText(outputPath);
        Assert.Equal(string.Join('\n',
        [
            "Artist: Test Artist",
            "Song: Test Bass",
            "Tuning: E1 A1 D2 G2 B2 E3",
            string.Empty,
            "e|-|",
            "B|-|",
            "G|-|",
            "D|-|",
            "A|-|",
            "E|3|",
            string.Empty
        ]), tab);
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
        var output = Run("--in", inputPath, "--out", outputPath, "--arrangement", "2");

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

        var output = Run("--in", inputPath, "--out", outputPath, "--arrangement", "2");

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
        Assert.Contains("import arrangement from", output.StandardError);
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

    /// <summary>A minimal bass arrangement used to verify archive-entry selection.</summary>
    private const string BassArrangementXml = """
        <song version="7">
          <title>Test Bass</title><arrangement>Bass</arrangement><artistName>Test Artist</artistName><offset>0</offset>
          <tuning string0="0" string1="0" string2="0" string3="0" string4="0" string5="0" />
          <chordTemplates />
          <levels><level difficulty="0"><notes><note time="1.25" sustain="0.5" string="0" fret="3" /></notes><chords /></level></levels>
        </song>
        """;

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
