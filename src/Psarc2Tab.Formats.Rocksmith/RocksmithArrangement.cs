namespace Psarc2Tab.Formats.Rocksmith;

/// <summary>Describes an arrangement discovered in a Rocksmith archive.</summary>
/// <param name="Number">The one-based position shown by the CLI.</param>
/// <param name="Instrument">The instrument or arrangement label from the archive metadata.</param>
/// <param name="ArchivePath">The archive path containing the chart payload.</param>
/// <param name="DataFormat">The format used to store the arrangement chart.</param>
/// <param name="MetadataPath">The optional archive path containing arrangement metadata.</param>
/// <param name="SngMetadata">The song metadata required to interpret an SNG chart.</param>
public sealed record RocksmithArrangement(
    int Number,
    string Instrument,
    string ArchivePath,
    RocksmithArrangementFormat DataFormat = RocksmithArrangementFormat.Xml,
    string? MetadataPath = null,
    RocksmithSngMetadata? SngMetadata = null);

/// <summary>Stores the song identity and six tuning offsets declared by an SNG arrangement manifest.</summary>
/// <param name="Title">The displayed song title.</param>
/// <param name="Artist">The displayed artist name.</param>
/// <param name="TuningOffsets">The six semitone offsets in low-to-high string order.</param>
/// <param name="CentOffsetCents">The arrangement pitch offset in cents.</param>
public sealed record RocksmithSngMetadata(string Title, string Artist, IReadOnlyList<int> TuningOffsets,
    decimal CentOffsetCents = 0);

/// <summary>Identifies the chart payload format used by a Rocksmith arrangement.</summary>
public enum RocksmithArrangementFormat
{
    /// <summary>The chart is stored as an XML arrangement document.</summary>
    Xml,

    /// <summary>The chart is stored as an encrypted SNG payload.</summary>
    Sng
}
