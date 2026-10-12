namespace Psarc2Tab.Formats.Rocksmith;

/// <summary>Identifies the archive entry and format used to import song lyrics.</summary>
/// <param name="ArchivePath">The path of the vocal chart inside the archive.</param>
/// <param name="DataFormat">The format of the vocal chart.</param>
public sealed record RocksmithLyricsSource(string ArchivePath, RocksmithArrangementFormat DataFormat);
