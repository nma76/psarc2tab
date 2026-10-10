namespace Psarc2Tab.Domain;

/// <summary>Represents a timed group of fretted notes played together.</summary>
public sealed class ChordEvent
{
    /// <summary>Creates a chord event with one or more fretted notes.</summary>
    /// <param name="position">The chord start time relative to the song beginning.</param>
    /// <param name="duration">The chord duration.</param>
    /// <param name="notes">The fretted notes forming the chord.</param>
    /// <param name="label">An optional chord name.</param>
    /// <exception cref="ArgumentException">No notes are supplied or a string occurs more than once.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="notes"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A time is negative.</exception>
    public ChordEvent(TimeSpan position, TimeSpan duration, IEnumerable<FrettedNote> notes, string? label = null)
    {
        ArgumentNullException.ThrowIfNull(notes);
        if (position < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(position), position, "Timeline values cannot be negative.");
        }

        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "Timeline values cannot be negative.");
        }

        var noteArray = notes.ToArray();
        if (noteArray.Length == 0)
        {
            throw new ArgumentException("A chord must contain at least one fretted note.", nameof(notes));
        }

        if (noteArray.Any(note => note.StringNumber.Value <= 0 || note.FretNumber.Value < 0))
        {
            throw new ArgumentException("Chord notes must have valid string and fret positions.", nameof(notes));
        }

        if (noteArray.Select(note => note.StringNumber).Distinct().Count() != noteArray.Length)
        {
            throw new ArgumentException("A chord cannot contain multiple notes on the same string.", nameof(notes));
        }

        Position = position;
        Duration = duration;
        Notes = Array.AsReadOnly(noteArray);
        Label = label;
    }

    /// <summary>Gets the chord start time relative to the song beginning.</summary>
    public TimeSpan Position { get; }

    /// <summary>Gets the chord duration.</summary>
    public TimeSpan Duration { get; }

    /// <summary>Gets the fretted notes forming the chord.</summary>
    public IReadOnlyList<FrettedNote> Notes { get; }

    /// <summary>Gets the optional chord name.</summary>
    public string? Label { get; }
}
