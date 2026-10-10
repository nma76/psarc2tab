namespace Psarc2Tab.Formats.PSARC;

/// <summary>Stores the validated metadata from a supported PSARC header.</summary>
/// <param name="Version">The PSARC format version.</param>
/// <param name="CompressionMethod">The archive compression method.</param>
/// <param name="TocSize">The total TOC region size, including the header.</param>
/// <param name="TocEntrySize">The size of each TOC entry in bytes.</param>
/// <param name="FileCount">The number of file entries in the archive.</param>
/// <param name="BlockSize">The uncompressed block size in bytes.</param>
/// <param name="ArchiveFlags">The raw archive flag bits.</param>
public sealed record PsarcHeader(
    PsarcVersion Version,
    string CompressionMethod,
    uint TocSize,
    uint TocEntrySize,
    uint FileCount,
    uint BlockSize,
    uint ArchiveFlags);
