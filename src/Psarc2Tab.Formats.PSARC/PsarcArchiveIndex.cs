using System.Collections.ObjectModel;

namespace Psarc2Tab.Formats.PSARC;

/// <summary>Contains the file records and compressed block sizes from a PSARC TOC.</summary>
public sealed class PsarcArchiveIndex
{
    /// <summary>Creates a read-only index from parsed TOC records.</summary>
    /// <param name="entries">The archive's file records in TOC order.</param>
    /// <param name="blockSizes">The compressed sizes for each data block.</param>
    /// <param name="archiveStart">The stream offset where the archive began.</param>
    internal PsarcArchiveIndex(PsarcIndexEntry[] entries, ushort[] blockSizes, long archiveStart)
    {
        Entries = Array.AsReadOnly(entries);
        BlockSizes = Array.AsReadOnly(blockSizes);
        ArchiveStart = archiveStart;
    }

    /// <summary>Gets file records in the same order as their TOC entries.</summary>
    public ReadOnlyCollection<PsarcIndexEntry> Entries { get; }

    /// <summary>Gets compressed block sizes; zero denotes an uncompressed block.</summary>
    public ReadOnlyCollection<ushort> BlockSizes { get; }

    /// <summary>Gets the stream offset where the archive began.</summary>
    internal long ArchiveStart { get; }
}
