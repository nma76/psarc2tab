using Psarc2Tab.Cli;

namespace Psarc2Tab.Cli.Tests;

/// <summary>Verifies command parsing for supported and invalid arguments.</summary>
public sealed class CommandLineParserTests
{
    /// <summary>The parser used by each test case.</summary>
    private readonly CommandLineParser _parser = new();

    /// <summary>Parses input and output options into a conversion command.</summary>
    [Fact]
    public void Parse_ConversionOptions_ReturnsPaths()
    {
        var result = _parser.Parse(["--in", "song.psarc", "--out", "song.tab"]);

        Assert.Equal(CommandKind.Convert, result.Kind);
        Assert.Equal("song.psarc", result.InputPath);
        Assert.Equal("song.tab", result.OutputPath);
        Assert.Equal("tab", result.FormatName);
        Assert.True(result.AllArrangements);
    }

    /// <summary>Parses a named output format for an arrangement.</summary>
    [Fact]
    public void Parse_FormatOption_ReturnsFormatName()
    {
        var result = _parser.Parse(["--in", "song.psarc", "--out", "song.mid", "--format", "midi"]);

        Assert.Equal(CommandKind.Convert, result.Kind);
        Assert.Equal("midi", result.FormatName);
    }

    /// <summary>Allows heatmap format to use stdout when no output path is supplied.</summary>
    [Fact]
    public void Parse_HeatmapWithoutOutputPath_ReturnsScreenConversion()
    {
        var result = _parser.Parse(["--in", "song.psarc", "--format", "heatmap", "--arr", "2"]);

        Assert.Equal(CommandKind.Convert, result.Kind);
        Assert.Equal("song.psarc", result.InputPath);
        Assert.Null(result.OutputPath);
        Assert.Equal("heatmap", result.FormatName);
        Assert.Equal(2, result.ArrangementNumber);
    }

    /// <summary>Accepts input and output options in either order.</summary>
    [Fact]
    public void Parse_ConversionOptionsInReverseOrder_ReturnsPaths()
    {
        var result = _parser.Parse(["--out", "song.tab", "--in", "song.psarc"]);

        Assert.Equal(CommandKind.Convert, result.Kind);
        Assert.Equal("song.psarc", result.InputPath);
        Assert.Equal("song.tab", result.OutputPath);
    }

    /// <summary>Rejects single-dash options.</summary>
    [Fact]
    public void Parse_SingleDashOptions_IsInvalid()
    {
        var result = _parser.Parse(["-in", "song.psarc", "-out", "song.tab"]);

        Assert.Equal(CommandKind.Invalid, result.Kind);
        Assert.Contains("Unknown option", result.Error);
    }

    /// <summary>Parses a numbered arrangement listing request.</summary>
    [Fact]
    public void Parse_ListArrangements_ReturnsInputArchive()
    {
        var result = _parser.Parse(["--in", "song.psarc", "--list"]);

        Assert.Equal(CommandKind.ListArrangements, result.Kind);
        Assert.Equal("song.psarc", result.InputPath);
    }

    /// <summary>Parses archive statistics with an optional arrangement selection.</summary>
    [Fact]
    public void Parse_Statistics_WithArrangement_ReturnsStatisticsCommand()
    {
        var result = _parser.Parse(["--in", "song.psarc", "--stats", "--arr", "2"]);

        Assert.Equal(CommandKind.Statistics, result.Kind);
        Assert.True(result.ShowStatistics);
        Assert.Equal(2, result.ArrangementNumber);
    }

    /// <summary>Rejects statistics requests that also ask for a file output.</summary>
    [Theory]
    [InlineData("--in song.psarc --stats --out song.tab")]
    [InlineData("--in song.psarc --stats --format midi")]
    [InlineData("--in song.psarc --stats --list")]
    public void Parse_StatisticsWithOutputOrListing_IsInvalid(string arguments)
    {
        Assert.Equal(CommandKind.Invalid,
            _parser.Parse(arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Kind);
    }

    /// <summary>Parses an arrangement number for a conversion request.</summary>
    [Fact]
    public void Parse_ArrangementSelection_ReturnsNumber()
    {
        var result = _parser.Parse(["--in", "song.psarc", "--out", "song.tab", "--arr", "2"]);

        Assert.Equal(CommandKind.Convert, result.Kind);
        Assert.Equal(2, result.ArrangementNumber);
        Assert.False(result.AllArrangements);
    }

    /// <summary>Parses the all-arrangements selection.</summary>
    [Theory]
    [InlineData("all")]
    [InlineData("ALL")]
    public void Parse_AllArrangements_SetsSelection(string value)
    {
        var result = _parser.Parse(["--in", "song.psarc", "--out", "song.tab", "--arr", value]);

        Assert.Equal(CommandKind.Convert, result.Kind);
        Assert.True(result.AllArrangements);
        Assert.Null(result.ArrangementNumber);
    }

    /// <summary>Rejects invalid arrangement numbers and incompatible list options.</summary>
    /// <param name="arguments">An invalid arrangement-related command.</param>
    [Theory]
    [InlineData("--in song.psarc --out song.tab --arr 0")]
    [InlineData("--in song.psarc --out song.tab --arr one")]
    [InlineData("--in song.psarc --out song.tab --arr everything")]
    [InlineData("--list")]
    [InlineData("--in song.psarc --out song.tab --list")]
    [InlineData("--in song.psarc --list --arr 1")]
    [InlineData("--in song.psarc --list --format tab")]
    [InlineData("--in song.psarc --out song.tab --arrangement 1")]
    public void Parse_InvalidArrangementCommand_IsInvalid(string arguments)
    {
        var result = _parser.Parse(arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        Assert.Equal(CommandKind.Invalid, result.Kind);
    }

    /// <summary>Rejects conversion options without path values.</summary>
    /// <param name="option">The option that has no following path.</param>
    [Theory]
    [InlineData("--in")]
    [InlineData("--out")]
    [InlineData("--format")]
    public void Parse_OptionWithoutValue_IsInvalid(string option)
    {
        var result = _parser.Parse([option]);

        Assert.Equal(CommandKind.Invalid, result.Kind);
        Assert.Contains(option == "--format" ? "format name" : "path", result.Error);
    }

    /// <summary>Rejects a repeated format option.</summary>
    [Fact]
    public void Parse_DuplicateFormatOption_IsInvalid()
    {
        var result = _parser.Parse(["--in", "song.psarc", "--out", "song.tab", "--format", "tab", "--format", "midi"]);

        Assert.Equal(CommandKind.Invalid, result.Kind);
        Assert.Contains("format option can only be specified once", result.Error);
    }

    /// <summary>Rejects a conversion request that omits its output option.</summary>
    [Fact]
    public void Parse_MissingRequiredOption_IsInvalid()
    {
        var result = _parser.Parse(["--in", "song.psarc"]);

        Assert.Equal(CommandKind.Invalid, result.Kind);
        Assert.Contains("'--out' is required", result.Error);
    }

    /// <summary>Rejects duplicate input options.</summary>
    [Fact]
    public void Parse_DuplicateOption_IsInvalid()
    {
        var result = _parser.Parse(["--in", "one.psarc", "--in", "two.psarc", "--out", "song.tab"]);

        Assert.Equal(CommandKind.Invalid, result.Kind);
        Assert.Contains("can only be specified once", result.Error);
    }

    /// <summary>Rejects options that are not supported.</summary>
    [Fact]
    public void Parse_UnknownOption_IsInvalid()
    {
        var result = _parser.Parse(["--input", "song.psarc"]);

        Assert.Equal(CommandKind.Invalid, result.Kind);
        Assert.Contains("Unknown option", result.Error);
    }

    /// <summary>Rejects simultaneous help and version requests.</summary>
    [Fact]
    public void Parse_HelpAndVersionTogether_IsInvalid()
    {
        var result = _parser.Parse(["--help", "--version"]);

        Assert.Equal(CommandKind.Invalid, result.Kind);
    }
}
