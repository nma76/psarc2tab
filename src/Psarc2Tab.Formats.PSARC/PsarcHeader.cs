namespace Psarc2Tab.Formats.PSARC;

public sealed record PsarcHeader(
    PsarcVersion Version,
    string CompressionMethod,
    uint TocSize,
    uint TocEntrySize,
    uint FileCount,
    uint BlockSize,
    uint ArchiveFlags);
