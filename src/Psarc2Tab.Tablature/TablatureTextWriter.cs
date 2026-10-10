using System.Globalization;
using System.Text.Json;
using Psarc2Tab.Domain;

namespace Psarc2Tab.Tablature;

/// <summary>Writes the version 1 psarc2tab tab-separated text format.</summary>
public sealed class TablatureTextWriter
{
    /// <summary>Writes song metadata, tuning, and time-ordered note rows.</summary>
    /// <param name="song">The song that owns the selected track.</param>
    /// <param name="track">The track to render.</param>
    /// <param name="writer">The destination text writer.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="track"/> is not part of <paramref name="song"/>.</exception>
    public void Write(Song song, Track track, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(song);
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(writer);
        if (!song.Tracks.Contains(track))
        {
            throw new ArgumentException("The selected track does not belong to the song.", nameof(track));
        }

        var lines = new List<string>
        {
            "# psarc2tab-tab/1",
            $"song\t{JsonSerializer.Serialize(song.Title)}",
            $"artist\t{JsonSerializer.Serialize(song.Artist)}",
            $"track\t{JsonSerializer.Serialize(track.Name)}",
            $"tuning_midi\t{string.Join(",", track.Tuning.OpenStringMidiPitches)}",
            "position_ms\tstring\tfret\tduration_ms\tevent\ttechniques\tchord_label"
        };

        foreach (var tabEvent in GetOrderedEvents(track))
        {
            lines.Add(string.Join('\t',
                FormatMilliseconds(tabEvent.Position),
                tabEvent.StringNumber.ToString(CultureInfo.InvariantCulture),
                tabEvent.FretNumber.ToString(CultureInfo.InvariantCulture),
                FormatMilliseconds(tabEvent.Duration),
                tabEvent.IsChord ? "chord" : "note",
                FormatTechniques(tabEvent.Techniques),
                JsonSerializer.Serialize(tabEvent.ChordLabel ?? string.Empty)));
        }

        writer.Write(string.Join('\n', lines));
        writer.Write('\n');
    }

    /// <summary>Creates a stable time and string ordering for notes and chord tones.</summary>
    /// <param name="track">The track whose events are ordered.</param>
    /// <returns>Events ordered by position and then string number.</returns>
    private static IEnumerable<TabEvent> GetOrderedEvents(Track track)
    {
        var events = new List<TabEvent>();
        foreach (var note in track.Notes)
        {
            events.Add(new TabEvent(
                note.Position,
                note.StringNumber.Value,
                note.FretNumber.Value,
                note.Duration,
                note.Techniques,
                false,
                null));
        }

        foreach (var chord in track.Chords)
        {
            foreach (var note in chord.Notes)
            {
                events.Add(new TabEvent(
                    chord.Position,
                    note.StringNumber.Value,
                    note.FretNumber.Value,
                    chord.Duration,
                    NoteTechnique.None,
                    true,
                    chord.Label));
            }
        }

        return events.OrderBy(tabEvent => tabEvent.Position).ThenBy(tabEvent => tabEvent.StringNumber);
    }

    /// <summary>Formats a duration in milliseconds without losing TimeSpan tick precision.</summary>
    /// <param name="time">The timeline value to format.</param>
    /// <returns>An invariant decimal millisecond value.</returns>
    private static string FormatMilliseconds(TimeSpan time) =>
        ((decimal)time.Ticks / TimeSpan.TicksPerMillisecond).ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>Formats combined note techniques in enum declaration order.</summary>
    /// <param name="techniques">The technique flags to format.</param>
    /// <returns>The technique names joined with a vertical bar, or <c>None</c>.</returns>
    private static string FormatTechniques(NoteTechnique techniques)
    {
        var names = Enum.GetValues<NoteTechnique>()
            .Where(technique => technique != NoteTechnique.None && (techniques & technique) != 0)
            .Select(technique => technique.ToString());
        var result = string.Join('|', names);
        return result.Length == 0 ? nameof(NoteTechnique.None) : result;
    }

    /// <summary>Stores one rendered note or chord tone.</summary>
    /// <param name="Position">The event position.</param>
    /// <param name="StringNumber">The one-based string position.</param>
    /// <param name="FretNumber">The fret position.</param>
    /// <param name="Duration">The event duration.</param>
    /// <param name="Techniques">The note techniques.</param>
    /// <param name="IsChord">Whether the row belongs to a chord.</param>
    /// <param name="ChordLabel">The chord label, if any.</param>
    private sealed record TabEvent(
        TimeSpan Position,
        int StringNumber,
        int FretNumber,
        TimeSpan Duration,
        NoteTechnique Techniques,
        bool IsChord,
        string? ChordLabel);
}
