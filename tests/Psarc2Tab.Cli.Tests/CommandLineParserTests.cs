using Psarc2Tab.Cli;

namespace Psarc2Tab.Cli.Tests;

public sealed class CommandLineParserTests
{
    private readonly CommandLineParser _parser = new();

    [Fact]
    public void Parse_ConversionOptions_ReturnsPaths()
    {
        var result = _parser.Parse(["--in", "song.psarc", "--out", "song.tab"]);

        Assert.Equal(CommandKind.Convert, result.Kind);
        Assert.Equal("song.psarc", result.InputPath);
        Assert.Equal("song.tab", result.OutputPath);
    }

    [Fact]
    public void Parse_ConversionOptionsInReverseOrder_ReturnsPaths()
    {
        var result = _parser.Parse(["--out", "song.tab", "--in", "song.psarc"]);

        Assert.Equal(CommandKind.Convert, result.Kind);
        Assert.Equal("song.psarc", result.InputPath);
        Assert.Equal("song.tab", result.OutputPath);
    }

    [Theory]
    [InlineData("--in")]
    [InlineData("--out")]
    public void Parse_OptionWithoutValue_IsInvalid(string option)
    {
        var result = _parser.Parse([option]);

        Assert.Equal(CommandKind.Invalid, result.Kind);
        Assert.Contains("requires a path", result.Error);
    }

    [Fact]
    public void Parse_MissingRequiredOption_IsInvalid()
    {
        var result = _parser.Parse(["--in", "song.psarc"]);

        Assert.Equal(CommandKind.Invalid, result.Kind);
    }

    [Fact]
    public void Parse_DuplicateOption_IsInvalid()
    {
        var result = _parser.Parse(["--in", "one.psarc", "--in", "two.psarc", "--out", "song.tab"]);

        Assert.Equal(CommandKind.Invalid, result.Kind);
        Assert.Contains("only be specified once", result.Error);
    }

    [Fact]
    public void Parse_UnknownOption_IsInvalid()
    {
        var result = _parser.Parse(["--input", "song.psarc"]);

        Assert.Equal(CommandKind.Invalid, result.Kind);
        Assert.Contains("Unknown option", result.Error);
    }

    [Fact]
    public void Parse_HelpAndVersionTogether_IsInvalid()
    {
        var result = _parser.Parse(["--help", "--version"]);

        Assert.Equal(CommandKind.Invalid, result.Kind);
    }
}
