namespace Psarc2Tab.Domain;

/// <summary>Stores open-string MIDI pitches in string-number order.</summary>
public sealed class GuitarTuning
{
    /// <summary>Creates a tuning from MIDI pitches ordered by string number.</summary>
    /// <param name="openStringMidiPitches">The MIDI pitch for each open string.</param>
    /// <exception cref="ArgumentException">No strings are supplied or a pitch is outside the MIDI range.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="openStringMidiPitches"/> is null.</exception>
    public GuitarTuning(IEnumerable<int> openStringMidiPitches)
    {
        ArgumentNullException.ThrowIfNull(openStringMidiPitches);

        var pitches = openStringMidiPitches.ToArray();
        if (pitches.Length == 0)
        {
            throw new ArgumentException("A tuning must define at least one string.", nameof(openStringMidiPitches));
        }

        if (pitches.Any(pitch => pitch is < 0 or > 127))
        {
            throw new ArgumentOutOfRangeException(nameof(openStringMidiPitches), "MIDI pitches must be between 0 and 127.");
        }

        OpenStringMidiPitches = Array.AsReadOnly(pitches);
    }

    /// <summary>Gets each open-string MIDI pitch in string-number order.</summary>
    public IReadOnlyList<int> OpenStringMidiPitches { get; }

    /// <summary>Gets the number of strings in this tuning.</summary>
    public int StringCount => OpenStringMidiPitches.Count;
}
