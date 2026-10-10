namespace Psarc2Tab.Domain;

/// <summary>Represents a note by its one-based string and fret positions.</summary>
/// <param name="StringNumber">The one-based string number in tuning order.</param>
/// <param name="FretNumber">The fret number, where zero is an open string.</param>
public readonly record struct FrettedNote(StringNumber StringNumber, FretNumber FretNumber);
