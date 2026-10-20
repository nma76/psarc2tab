using Psarc2Tab.Domain;

namespace Psarc2Tab.Application;

/// <summary>Contains arrangement counts and ranges calculated from imported song events.</summary>
/// <param name="NoteCount">The number of standalone note events.</param>
/// <param name="ChordCount">The number of chord events.</param>
/// <param name="SectionCount">The number of imported sections.</param>
/// <param name="BendCount">Standalone notes carrying a bend technique.</param>
/// <param name="SlideCount">Standalone notes carrying a slide technique.</param>
/// <param name="HammerOnCount">Standalone notes carrying a hammer-on technique.</param>
/// <param name="PullOffCount">Standalone notes carrying a pull-off technique.</param>
/// <param name="UsedStrings">Distinct string positions used by notes or chord tones.</param>
/// <param name="MinimumFret">The lowest fret used by notes or chord tones.</param>
/// <param name="MaximumFret">The highest fret used by notes or chord tones.</param>
/// <param name="Length">The latest event end, or onset when the event has no duration.</param>
public sealed record ArrangementStatistics(int NoteCount, int ChordCount, int SectionCount,
    int BendCount, int SlideCount, int HammerOnCount, int PullOffCount, IReadOnlyList<int> UsedStrings,
    int? MinimumFret, int? MaximumFret, TimeSpan Length);

/// <summary>Calculates deterministic statistics from an imported song.</summary>
public static class ArrangementStatisticsCalculator
{
    /// <summary>Calculates event counts, techniques, positions, and latest event time.</summary>
    /// <param name="song">The imported song containing one or more tracks.</param>
    /// <returns>Statistics derived only from events present in the song.</returns>
    public static ArrangementStatistics Calculate(Song song)
    {
        ArgumentNullException.ThrowIfNull(song);
        var notes = song.Tracks.SelectMany(track => track.Notes).ToArray();
        var chords = song.Tracks.SelectMany(track => track.Chords).ToArray();
        var chordNotes = chords.SelectMany(chord => chord.Notes).ToArray();
        var strings = notes.Select(note => note.StringNumber.Value)
            .Concat(chordNotes.Select(note => note.StringNumber.Value)).Distinct().Order().ToArray();
        var frets = notes.Select(note => note.FretNumber.Value)
            .Concat(chordNotes.Select(note => note.FretNumber.Value)).ToArray();
        var length = notes.Select(note => GetEventEnd(note.Position, note.Duration))
            .Concat(chords.Select(chord => GetEventEnd(chord.Position, chord.Duration)))
            .DefaultIfEmpty(TimeSpan.Zero).Max();

        return new ArrangementStatistics(notes.Length, chords.Length, song.Sections.Count,
            CountTechnique(notes, NoteTechnique.Bend), CountTechnique(notes, NoteTechnique.Slide),
            CountTechnique(notes, NoteTechnique.HammerOn), CountTechnique(notes, NoteTechnique.PullOff),
            Array.AsReadOnly(strings), frets.Length == 0 ? null : frets.Min(),
            frets.Length == 0 ? null : frets.Max(), length);
    }

    /// <summary>Counts note events carrying a requested technique flag.</summary>
    /// <param name="notes">The standalone note events.</param>
    /// <param name="technique">The technique to count.</param>
    /// <returns>The number of matching events.</returns>
    private static int CountTechnique(IEnumerable<NoteEvent> notes, NoteTechnique technique) =>
        notes.Count(note => (note.Techniques & technique) != 0);

    /// <summary>Calculates an event end without overflowing the representable timeline.</summary>
    /// <param name="position">The event start.</param>
    /// <param name="duration">The event duration.</param>
    /// <returns>The end time, saturated at the largest supported timeline value.</returns>
    private static TimeSpan GetEventEnd(TimeSpan position, TimeSpan duration) =>
        duration > TimeSpan.MaxValue - position ? TimeSpan.MaxValue : position + duration;
}
