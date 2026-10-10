namespace Psarc2Tab.Domain;

/// <summary>Represents one instrument arrangement with its tuning and events.</summary>
public sealed class Track
{
    /// <summary>Creates a track and validates its notes against its tuning.</summary>
    /// <param name="name">The arrangement or instrument name.</param>
    /// <param name="tuning">The open-string pitches in string-number order.</param>
    /// <param name="notes">The individual fretted notes.</param>
    /// <param name="chords">The simultaneous fretted-note groups.</param>
    /// <exception cref="ArgumentException">The name is blank or an event refers to a string absent from the tuning.</exception>
    /// <exception cref="ArgumentNullException">A required argument or event is null.</exception>
    public Track(string name, GuitarTuning tuning, IEnumerable<NoteEvent> notes, IEnumerable<ChordEvent> chords)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(tuning);
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(chords);

        var noteArray = notes.ToArray();
        var chordArray = chords.ToArray();
        if (noteArray.Any(note => note is null) || chordArray.Any(chord => chord is null))
        {
            throw new ArgumentException("Track events cannot contain null values.");
        }

        if (noteArray.Any(note => IsOutsideTuning(note.StringNumber, tuning.StringCount)) ||
            chordArray.SelectMany(chord => chord.Notes).Any(note => IsOutsideTuning(note.StringNumber, tuning.StringCount)))
        {
            throw new ArgumentException("A track event refers to a string that is absent from the tuning.");
        }

        Name = name;
        Tuning = tuning;
        Notes = Array.AsReadOnly(noteArray);
        Chords = Array.AsReadOnly(chordArray);
    }

    /// <summary>Gets the arrangement or instrument name.</summary>
    public string Name { get; }

    /// <summary>Gets the open-string tuning in string-number order.</summary>
    public GuitarTuning Tuning { get; }

    /// <summary>Gets the individual note events in source order.</summary>
    public IReadOnlyList<NoteEvent> Notes { get; }

    /// <summary>Gets the chord events in source order.</summary>
    public IReadOnlyList<ChordEvent> Chords { get; }

    /// <summary>Checks whether a string number fits the track tuning.</summary>
    /// <param name="stringNumber">The one-based string position to check.</param>
    /// <param name="stringCount">The number of strings in the tuning.</param>
    /// <returns><see langword="true"/> when the string position is invalid for the tuning.</returns>
    private static bool IsOutsideTuning(StringNumber stringNumber, int stringCount) =>
        stringNumber.Value < 1 || stringNumber.Value > stringCount;
}
