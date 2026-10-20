using Psarc2Tab.Formats.PSARC;

namespace Psarc2Tab.Formats.Rocksmith;

/// <summary>Contains archive header metadata and paths resolved from the Rocksmith manifest.</summary>
/// <param name="Header">The validated PSARC header.</param>
/// <param name="ArchivePaths">Non-manifest paths in PSARC index order.</param>
public sealed record RocksmithArchiveDetails(PsarcHeader Header, IReadOnlyList<string> ArchivePaths);
