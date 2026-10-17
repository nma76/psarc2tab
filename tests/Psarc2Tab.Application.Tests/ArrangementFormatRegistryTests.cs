using Psarc2Tab.Application;
using Psarc2Tab.Tablature;

namespace Psarc2Tab.Application.Tests;

/// <summary>Verifies arrangement format registration and name resolution.</summary>
public sealed class ArrangementFormatRegistryTests
{
    /// <summary>Resolves registered format names without regard to letter case.</summary>
    [Fact]
    public void TryGetWriter_FormatNameIsCaseInsensitive_ReturnsWriter()
    {
        var writer = new TablatureTextWriter();
        var registry = new ArrangementFormatRegistry([writer]);

        Assert.True(registry.TryGetWriter("TAB", out var resolved));
        Assert.Same(writer, resolved);
        Assert.Equal(["tab"], registry.SupportedFormatNames);
    }

    /// <summary>Rejects multiple writers registered under the same format name.</summary>
    [Fact]
    public void Constructor_DuplicateFormatName_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new ArrangementFormatRegistry(
            [new TablatureTextWriter(), new TablatureTextWriter()]));
    }
}
