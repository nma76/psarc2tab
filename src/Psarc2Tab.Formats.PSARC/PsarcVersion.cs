namespace Psarc2Tab.Formats.PSARC;

/// <summary>Represents the major and minor version encoded in a PSARC header.</summary>
/// <param name="Major">The major format version.</param>
/// <param name="Minor">The minor format version.</param>
public readonly record struct PsarcVersion(ushort Major, ushort Minor)
{
    /// <summary>Returns the version as a dotted major and minor value.</summary>
    /// <returns>The version in <c>major.minor</c> form.</returns>
    public override string ToString() => $"{Major}.{Minor}";
}
