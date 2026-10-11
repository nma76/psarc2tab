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

        writer.Write("Artist: ");
        writer.Write(song.Artist);
        writer.Write('\n');
        writer.Write("Song: ");
        writer.Write(song.Title);
        writer.Write('\n');
        writer.Write("Tuning: ");
        writer.Write(FormatTuning(track.Tuning));
        writer.Write("\n\n");

        var columns = CreateColumns(track);
        var segments = CreateSegments(columns, song.Sections);
        var labels = Enumerable.Range(1, track.Tuning.StringCount)
            .Select(stringNumber => GetStringLabel(track.Tuning.OpenStringMidiPitches[stringNumber - 1],
                stringNumber == track.Tuning.StringCount))
            .ToArray();
        var labelWidth = labels.Max(label => label.Length);

        for (var segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
        {
            var segment = segments[segmentIndex];
            if (segment.Heading is not null)
            {
                writer.Write('[');
                writer.Write(segment.Heading);
                writer.Write("]\n");
            }

            var rows = CreateRows(segment.Columns);
            WriteRows(writer, rows, labels, labelWidth, track.Tuning.StringCount);
            if (segmentIndex < segments.Count - 1)
            {
                writer.Write('\n');
            }
        }
    }

    /// <summary>Writes all string lines in a sequence of wrapped rows.</summary>
    /// <param name="writer">The destination writer.</param>
    /// <param name="rows">The tablature rows.</param>
    /// <param name="labels">The string labels in tuning order.</param>
    /// <param name="labelWidth">The width reserved for each string label.</param>
    /// <param name="stringCount">The number of string rows.</param>
    private static void WriteRows(TextWriter writer, IReadOnlyList<IReadOnlyList<TabColumn>> rows,
        IReadOnlyList<string> labels, int labelWidth, int stringCount)
    {
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            for (var stringNumber = stringCount; stringNumber >= 1; stringNumber--)
            {
                writer.Write(labels[stringNumber - 1].PadRight(labelWidth));
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

    /// <summary>Splits event columns at section boundaries and assigns readable headings.</summary>
    /// <param name="columns">The time-ordered tablature columns.</param>
    /// <param name="sections">The song section markers.</param>
    /// <returns>Tablature segments in song order.</returns>
    private static IReadOnlyList<TabSegment> CreateSegments(IReadOnlyList<TabColumn> columns,
        IReadOnlyList<SongSection> sections)
    {
        if (sections.Count == 0)
        {
            return [new TabSegment(null, columns)];
        }

        var orderedSections = sections.OrderBy(section => section.StartTime).ToArray();
        var repeatedNames = sections.GroupBy(section => section.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var segments = new List<TabSegment>();
        var firstStart = orderedSections[0].StartTime;
        var leadingColumns = columns.Where(column => column.Position < firstStart).ToArray();
        if (leadingColumns.Length > 0)
        {
            segments.Add(new TabSegment(null, leadingColumns));
        }

        var sectionGroups = orderedSections.GroupBy(section => section.StartTime).ToArray();
        for (var index = 0; index < sectionGroups.Length; index++)
        {
            var group = sectionGroups[index].ToArray();
            var start = sectionGroups[index].Key;
            var end = index + 1 < sectionGroups.Length ? sectionGroups[index + 1].Key : (TimeSpan?)null;
            var sectionColumns = columns.Where(column => column.Position >= start &&
                (end is null || column.Position < end.Value)).ToArray();
            var heading = string.Join(" / ", group.Select(section => FormatSectionName(section, repeatedNames)));
            segments.Add(new TabSegment(heading, sectionColumns));
        }

        return segments.AsReadOnly();
    }

    /// <summary>Formats a section name and adds its source number when the name repeats.</summary>
    /// <param name="section">The section marker.</param>
    /// <param name="repeatedNames">The names occurring more than once.</param>
    /// <returns>A title-cased section heading.</returns>
    private static string FormatSectionName(SongSection section, IReadOnlySet<string> repeatedNames)
    {
        var displayName = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(section.Name.ToLowerInvariant());
        return repeatedNames.Contains(section.Name) ? $"{displayName} {section.Number}" : displayName;
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
            columns.Add(new TabColumn(groups[index].Key, width, gapBefore, tokens));
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

    /// <summary>Formats open-string pitches from the lowest string to the highest string.</summary>
    /// <param name="tuning">The track tuning.</param>
    /// <returns>Space-separated note names with MIDI octave numbers.</returns>
    private static string FormatTuning(GuitarTuning tuning) => string.Join(' ',
        tuning.OpenStringMidiPitches.Select(pitch =>
            $"{PitchNames[pitch % PitchNames.Length]}{pitch / 12 - 1}"));

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
    /// <param name="Position">The event time represented by the column.</param>
    /// <param name="Width">The widest token in this column.</param>
    /// <param name="GapBefore">The horizontal spacing before this column.</param>
    /// <param name="Tokens">The tokens indexed by one-based string number.</param>
    private sealed record TabColumn(TimeSpan Position, int Width, int GapBefore, IReadOnlyDictionary<int, string> Tokens);

    /// <summary>Stores one labeled section span of tablature columns.</summary>
    /// <param name="Heading">The optional section heading.</param>
    /// <param name="Columns">The event columns inside the section span.</param>
    private sealed record TabSegment(string? Heading, IReadOnlyList<TabColumn> Columns);
}
