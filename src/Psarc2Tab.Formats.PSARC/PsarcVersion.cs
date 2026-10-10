namespace Psarc2Tab.Formats.PSARC;

public readonly record struct PsarcVersion(ushort Major, ushort Minor)
{
    public override string ToString() => $"{Major}.{Minor}";
}
