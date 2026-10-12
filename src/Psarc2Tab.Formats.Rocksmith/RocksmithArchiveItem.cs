namespace Psarc2Tab.Formats.Rocksmith;

/// <summary>Describes one user-selectable export item in a Rocksmith archive.</summary>
/// <param name="Number">The one-based selection number shown by the CLI.</param>
/// <param name="Name">The displayed arrangement or export item name.</param>
/// <param name="ArchivePath">The archive path containing the source data.</param>
/// <param name="IsLyrics">Whether the item exports lyrics instead of a guitar arrangement.</param>
public sealed record RocksmithArchiveItem(int Number, string Name, string ArchivePath, bool IsLyrics = false);
