using Psarc2Tab.Formats.PSARC;

namespace Psarc2Tab.Formats.Rocksmith;

/// <summary>Contains archive header metadata and paths resolved from the Rocksmith manifest.</summary>
/// <param name="Header">The validated PSARC header.</param>
/// <param name="ArchivePaths">Non-manifest paths in PSARC index order.</param>
public sealed record RocksmithArchiveDetails(PsarcHeader Header, IReadOnlyList<RocksmithArchiveEntry> Entries)
{
    /// <summary>Gets non-manifest paths in PSARC index order.</summary>
    public IReadOnlyList<string> ArchivePaths => Array.AsReadOnly(Entries.Select(entry => entry.Path).ToArray());
}

/// <summary>Describes one manifest path and its validated uncompressed index length.</summary>
/// <param name="Path">The normalized slash-separated path from the PSARC manifest.</param>
/// <param name="UncompressedLength">The payload length recorded in the PSARC index.</param>
public sealed record RocksmithArchiveEntry(string Path, ulong UncompressedLength);
