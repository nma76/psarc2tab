namespace Psarc2Tab.Domain;

/// <summary>Represents one timed fretted note.</summary>
public sealed class NoteEvent
{
    /// <summary>Creates a note event at a position on the song timeline.</summary>
    /// <param name="position">The start time relative to the beginning of the song.</param>
    /// <param name="duration">The note duration.</param>
    /// <param name="stringNumber">The one-based string position.</param>
    /// <param name="fretNumber">The fret position.</param>
    /// <param name="techniques">The techniques applied to the note.</param>
    /// <exception cref="ArgumentOutOfRangeException">A time is negative or a technique is unsupported.</exception>
    public NoteEvent(TimeSpan position, TimeSpan duration, StringNumber stringNumber, FretNumber fretNumber, NoteTechnique techniques = NoteTechnique.None)
    {
        ValidateTime(position, nameof(position));
        ValidateTime(duration, nameof(duration));
        ValidateTechniques(techniques);
        if (stringNumber.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stringNumber), stringNumber, "String numbers start at one.");
        }

        if (fretNumber.Value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fretNumber), fretNumber, "Fret numbers cannot be negative.");
        }

        Position = position;
        Duration = duration;
        StringNumber = stringNumber;
        FretNumber = fretNumber;
        Techniques = techniques;
    }

    /// <summary>Gets the note start time relative to the song beginning.</summary>
    public TimeSpan Position { get; }

    /// <summary>Gets the note duration.</summary>
    public TimeSpan Duration { get; }

    /// <summary>Gets the one-based string position.</summary>
    public StringNumber StringNumber { get; }

    /// <summary>Gets the fret position, where zero is an open string.</summary>
    public FretNumber FretNumber { get; }

    /// <summary>Gets the combined techniques applied to the note.</summary>
    public NoteTechnique Techniques { get; }

    /// <summary>Rejects negative timeline values.</summary>
    /// <param name="time">The time value to validate.</param>
    /// <param name="parameterName">The constructor parameter name.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="time"/> is negative.</exception>
    private static void ValidateTime(TimeSpan time, string parameterName)
    {
        if (time < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(parameterName, time, "Timeline values cannot be negative.");
        }
    }

    /// <summary>Rejects technique bits that are not represented by the current model.</summary>
    /// <param name="techniques">The technique flags to validate.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="techniques"/> contains unsupported flags.</exception>
    private static void ValidateTechniques(NoteTechnique techniques)
    {
        const NoteTechnique supported = NoteTechnique.HammerOn | NoteTechnique.PullOff | NoteTechnique.Bend |
            NoteTechnique.Slide | NoteTechnique.Vibrato | NoteTechnique.Harmonic | NoteTechnique.PalmMute |
            NoteTechnique.Accent | NoteTechnique.Tap | NoteTechnique.Slap | NoteTechnique.Pluck;
        if ((techniques & ~supported) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(techniques), techniques, "The note contains an unsupported technique flag.");
        }
    }
}
