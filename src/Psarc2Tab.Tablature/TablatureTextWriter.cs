using System.Globalization;
using Psarc2Tab.Application;
using Psarc2Tab.Domain;

namespace Psarc2Tab.Tablature;

/// <summary>Writes conventional six-line ASCII tablature from a domain track.</summary>
public sealed class TablatureTextWriter : ITablatureDocumentWriter
{
    /// <summary>The maximum event-grid width of one visual tablature row.</summary>
    private const int MaximumRowWidth = 64;

    /// <summary>The approximate timeline represented by one horizontal character.</summary>
    private static readonly TimeSpan TimePerColumn = TimeSpan.FromMilliseconds(125);

    /// <summary>Sharp note names indexed by MIDI pitch class.</summary>
    private static readonly string[] PitchNames = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    /// <inheritdoc />
    public void Write(Song song, Track track, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(song);
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(writer);
        if (!song.Tracks.Contains(track))
        {
            throw new ArgumentException("The selected track does not belong to the supplied song.", nameof(track));
        }

        var columns = CreateColumns(track);
        var rows = CreateRows(columns);
        var labels = Enumerable.Range(1, track.Tuning.StringCount)
            .Select(stringNumber => GetStringLabel(track.Tuning.OpenStringMidiPitches[stringNumber - 1],
                stringNumber == track.Tuning.StringCount))
            .ToArray();
        var labelWidth = labels.Max(label => label.Length);

        foreach (var rowIndex in Enumerable.Range(0, rows.Count))
        {
            for (var stringNumber = track.Tuning.StringCount; stringNumber >= 1; stringNumber--)
            {
                var label = labels[stringNumber - 1];
                writer.Write(label.PadRight(labelWidth));
                writer.Write('|');
                WriteStringRow(writer, rows[rowIndex], stringNumber);
                writer.Write('|');
                writer.Write('\n');
            }

            if (rowIndex < rows.Count - 1)
            {
                writer.Write('\n');
            }
        }
    }

    /// <summary>Builds time-ordered columns containing notes and chord tones.</summary>
    /// <param name="track">The track to render.</param>
    /// <returns>The ordered, width-checked tablature columns.</returns>
    private static IReadOnlyList<TabColumn> CreateColumns(Track track)
    {
        var events = new List<TabEvent>();
        foreach (var note in track.Notes)
        {
            events.Add(new TabEvent(note.Position, note.StringNumber.Value,
                FormatNote(note.FretNumber.Value, note.Techniques)));
        }

        foreach (var chord in track.Chords)
        {
            foreach (var note in chord.Notes)
            {
                events.Add(new TabEvent(chord.Position, note.StringNumber.Value,
                    note.FretNumber.Value.ToString(CultureInfo.InvariantCulture)));
            }
        }

        var groups = events
            .OrderBy(tabEvent => tabEvent.Position)
            .ThenBy(tabEvent => tabEvent.StringNumber)
            .GroupBy(tabEvent => tabEvent.Position)
            .ToArray();
        var columns = new List<TabColumn>(groups.Length);
        for (var index = 0; index < groups.Length; index++)
        {
            var tokens = new Dictionary<int, string>();
            foreach (var tabEvent in groups[index])
            {
                if (!tokens.TryAdd(tabEvent.StringNumber, tabEvent.Token))
                {
                    throw new InvalidOperationException(
                        $"Multiple tablature events occupy string {tabEvent.StringNumber} at {tabEvent.Position}.");
                }
            }

            var width = tokens.Values.Max(token => token.Length);
            var gapBefore = index == 0
                ? 0
                : GetGapWidth(groups[index - 1].Key, groups[index].Key);
            columns.Add(new TabColumn(width, gapBefore, tokens));
        }

        return columns.AsReadOnly();
    }

    /// <summary>Splits columns into visual rows that fit a readable terminal width.</summary>
    /// <param name="columns">The ordered tablature columns.</param>
    /// <returns>Rows of tablature columns, including one empty row for an empty track.</returns>
    private static IReadOnlyList<IReadOnlyList<TabColumn>> CreateRows(IReadOnlyList<TabColumn> columns)
    {
        if (columns.Count == 0)
        {
            return [Array.Empty<TabColumn>()];
        }

        var rows = new List<IReadOnlyList<TabColumn>>();
        var currentRow = new List<TabColumn>();
        var currentWidth = 0;
        foreach (var column in columns)
        {
            var gap = currentRow.Count == 0 ? 0 : column.GapBefore;
            if (currentRow.Count > 0 && currentWidth + gap + column.Width > MaximumRowWidth)
            {
                rows.Add(currentRow.AsReadOnly());
                currentRow = [];
                currentWidth = 0;
                gap = 0;
            }

            currentRow.Add(column with { GapBefore = gap });
            currentWidth += gap + column.Width;
        }

        rows.Add(currentRow.AsReadOnly());
        return rows.AsReadOnly();
    }

    /// <summary>Writes one string's tokens and timeline spacing for a visual row.</summary>
    /// <param name="writer">The destination writer.</param>
    /// <param name="columns">The columns in the visual row.</param>
    /// <param name="stringNumber">The one-based string position.</param>
    private static void WriteStringRow(TextWriter writer, IReadOnlyList<TabColumn> columns, int stringNumber)
    {
        for (var index = 0; index < columns.Count; index++)
        {
            var column = columns[index];
            if (index > 0)
            {
                writer.Write(new string('-', column.GapBefore));
            }

            var token = column.Tokens.GetValueOrDefault(stringNumber, string.Empty);
            writer.Write(token);
            writer.Write(new string('-', column.Width - token.Length));
        }
    }

    /// <summary>Maps a MIDI open-string pitch to its conventional ASCII label.</summary>
    /// <param name="midiPitch">The open-string MIDI pitch.</param>
    /// <param name="isHighestString">Whether this is the uppermost displayed string.</param>
    /// <returns>The pitch name, with a lowercase natural note for the highest string.</returns>
    private static string GetStringLabel(int midiPitch, bool isHighestString)
    {
        var name = PitchNames[midiPitch % PitchNames.Length];
        return isHighestString && name.Length == 1 ? name.ToLowerInvariant() : name;
    }

    /// <summary>Formats a fret and its supported playing techniques as common tab notation.</summary>
    /// <param name="fret">The fret number.</param>
    /// <param name="techniques">The techniques applied to the note.</param>
    /// <returns>A compact fret token.</returns>
    private static string FormatNote(int fret, NoteTechnique techniques)
    {
        if ((techniques & NoteTechnique.Muted) != 0)
        {
            return "x";
        }

        var fretText = fret.ToString(CultureInfo.InvariantCulture);
        var token = (techniques & NoteTechnique.Harmonic) != 0 ? $"<{fretText}>" : fretText;
        if ((techniques & NoteTechnique.Accent) != 0)
        {
            token = $">{token}";
        }

        if ((techniques & NoteTechnique.HammerOn) != 0) token += "h";
        if ((techniques & NoteTechnique.PullOff) != 0) token += "p";
        if ((techniques & NoteTechnique.Bend) != 0) token += "b";
        if ((techniques & NoteTechnique.Slide) != 0) token = $"su{token}";
        if ((techniques & NoteTechnique.Vibrato) != 0) token += "~";
        if ((techniques & NoteTechnique.PalmMute) != 0) token = $"PM{token}";
        if ((techniques & NoteTechnique.Tap) != 0) token += "t";
        if ((techniques & NoteTechnique.Slap) != 0) token += "sl";
        if ((techniques & NoteTechnique.Pluck) != 0) token += "pl";
        if ((techniques & NoteTechnique.Tremolo) != 0) token += "tr";
        return token;
    }

    /// <summary>Calculates a readable gap from the time between adjacent events.</summary>
    /// <param name="previousPosition">The earlier event position.</param>
    /// <param name="position">The later event position.</param>
    /// <returns>A gap between one and twelve hyphens.</returns>
    private static int GetGapWidth(TimeSpan previousPosition, TimeSpan position)
    {
        var columns = (position - previousPosition).Ticks / (double)TimePerColumn.Ticks;
        return (int)Math.Clamp(Math.Round(columns, MidpointRounding.AwayFromZero), 1d, 12d);
    }

    /// <summary>Stores one rendered note or chord tone.</summary>
    /// <param name="Position">The event's song position.</param>
    /// <param name="StringNumber">The one-based string position.</param>
    /// <param name="Token">The fret and technique notation.</param>
    private sealed record TabEvent(TimeSpan Position, int StringNumber, string Token);

    /// <summary>Stores the notes sharing a horizontal position.</summary>
    /// <param name="Width">The widest token in this column.</param>
    /// <param name="GapBefore">The horizontal spacing before this column.</param>
    /// <param name="Tokens">The tokens indexed by one-based string number.</param>
    private sealed record TabColumn(int Width, int GapBefore, IReadOnlyDictionary<int, string> Tokens);
}
