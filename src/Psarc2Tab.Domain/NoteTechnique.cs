namespace Psarc2Tab.Domain;

/// <summary>Flags the note techniques represented by the current domain model.</summary>
[Flags]
public enum NoteTechnique
{
    /// <summary>No technique is applied.</summary>
    None = 0,

    /// <summary>The note is played as a hammer-on.</summary>
    HammerOn = 1 << 0,

    /// <summary>The note is played as a pull-off.</summary>
    PullOff = 1 << 1,

    /// <summary>The note includes a pitch bend.</summary>
    Bend = 1 << 2,

    /// <summary>The note includes a slide.</summary>
    Slide = 1 << 3,

    /// <summary>The note is played with vibrato.</summary>
    Vibrato = 1 << 4,

    /// <summary>The note is played as a harmonic.</summary>
    Harmonic = 1 << 5,

    /// <summary>The note is played with palm muting.</summary>
    PalmMute = 1 << 6,

    /// <summary>The note is accented.</summary>
    Accent = 1 << 7,

    /// <summary>The note is tapped.</summary>
    Tap = 1 << 8,

    /// <summary>The note is slapped.</summary>
    Slap = 1 << 9,

    /// <summary>The note is plucked.</summary>
    Pluck = 1 << 10,

    /// <summary>The note is played with tremolo picking.</summary>
    Tremolo = 1 << 11,

    /// <summary>The string is muted.</summary>
    Muted = 1 << 12
}
