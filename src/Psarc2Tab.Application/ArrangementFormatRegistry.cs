namespace Psarc2Tab.Application;

/// <summary>Resolves supported arrangement output format names to their writers.</summary>
public sealed class ArrangementFormatRegistry
{
    /// <summary>The writers indexed by their case-insensitive format names.</summary>
    private readonly IReadOnlyDictionary<string, IArrangementFormatWriter> _writers;

    /// <summary>Creates a registry from the available format writers.</summary>
    /// <param name="writers">The writers to register.</param>
    /// <exception cref="ArgumentException">A writer is null, unnamed, or duplicates another format name.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="writers"/> is null.</exception>
    public ArrangementFormatRegistry(IEnumerable<IArrangementFormatWriter> writers)
    {
        ArgumentNullException.ThrowIfNull(writers);
        var writerArray = writers.ToArray();
        if (writerArray.Any(writer => writer is null || string.IsNullOrWhiteSpace(writer.FormatName)))
        {
            throw new ArgumentException("Format writers must be non-null and have a name.", nameof(writers));
        }

        var writerMap = new Dictionary<string, IArrangementFormatWriter>(StringComparer.OrdinalIgnoreCase);
        foreach (var writer in writerArray)
        {
            if (!writerMap.TryAdd(writer.FormatName, writer))
            {
                throw new ArgumentException($"The '{writer.FormatName}' format is registered more than once.", nameof(writers));
            }
        }

        _writers = writerMap;
    }

    /// <summary>Gets supported format names in deterministic case-insensitive order.</summary>
    public IReadOnlyList<string> SupportedFormatNames =>
        Array.AsReadOnly(_writers.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray());

    /// <summary>Finds the writer registered for a format name.</summary>
    /// <param name="formatName">The requested format name.</param>
    /// <param name="writer">The matching writer, when one is registered.</param>
    /// <returns><see langword="true"/> when the format is supported.</returns>
    public bool TryGetWriter(string formatName, out IArrangementFormatWriter? writer) =>
        _writers.TryGetValue(formatName, out writer);
}
