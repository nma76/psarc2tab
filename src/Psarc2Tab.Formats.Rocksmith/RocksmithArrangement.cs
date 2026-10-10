namespace Psarc2Tab.Formats.Rocksmith;

/// <summary>Describes an arrangement discovered in a Rocksmith archive.</summary>
/// <param name="Number">The one-based position shown by the CLI.</param>
/// <param name="Instrument">The instrument or arrangement label inferred from the file name.</param>
/// <param name="ArchivePath">The path recorded in the archive manifest.</param>
public sealed record RocksmithArrangement(int Number, string Instrument, string ArchivePath);
