namespace Psarc2Tab.Formats.PSARC;

/// <summary>Describes one file record in a PSARC table of contents.</summary>
public sealed class PsarcIndexEntry
{
    /// <summary>The immutable 16-byte digest used to identify the archive path.</summary>
    private readonly byte[] _pathDigest;

    /// <summary>Creates a file record from its decoded TOC fields.</summary>
    /// <param name="pathDigest">The 16-byte path digest.</param>
    /// <param name="firstBlockIndex">The first block-size-table index for the file.</param>
    /// <param name="uncompressedLength">The file length before block compression.</param>
    /// <param name="dataOffset">The file's first data offset relative to the archive.</param>
    internal PsarcIndexEntry(ReadOnlySpan<byte> pathDigest, uint firstBlockIndex, ulong uncompressedLength, ulong dataOffset)
    {
        _pathDigest = pathDigest.ToArray();
        FirstBlockIndex = firstBlockIndex;
        UncompressedLength = uncompressedLength;
        DataOffset = dataOffset;
    }

    /// <summary>Gets the path digest stored in the TOC record.</summary>
    public ReadOnlyMemory<byte> PathDigest => _pathDigest;

    /// <summary>Gets the first block-size-table index for this file.</summary>
    public uint FirstBlockIndex { get; }

    /// <summary>Gets the uncompressed file length in bytes.</summary>
    public ulong UncompressedLength { get; }

    /// <summary>Gets the first data offset relative to the archive start.</summary>
    public ulong DataOffset { get; }
}
