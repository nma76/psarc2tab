using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Psarc2Tab.Application;
using Psarc2Tab.Domain;

namespace Psarc2Tab.Formats.Rocksmith;

/// <summary>Converts a supported Rocksmith arrangement XML document to the song domain model.</summary>
public sealed class RocksmithArrangementImporter : IRocksmithArrangementImporter
{
    /// <summary>The maximum XML document size accepted by this importer.</summary>
    private const long MaximumDocumentCharacters = 64 * 1024 * 1024;

    /// <summary>Standard guitar open-string MIDI pitches ordered from low to high.</summary>
    private static readonly int[] GuitarOpenStringPitches = [40, 45, 50, 55, 59, 64];

    /// <inheritdoc />
    public Song Import(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
        {
            throw new ArgumentException("The arrangement stream must be readable.", nameof(source));
        }

        try
        {
            using var reader = XmlReader.Create(source, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaximumDocumentCharacters,
                CloseInput = false
            });
            var document = XDocument.Load(reader, LoadOptions.None);
            return ConvertDocument(document);
        }
        catch (XmlException exception)
        {
            throw new InvalidDataException($"The Rocksmith arrangement XML is malformed: {exception.Message}", exception);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException($"The Rocksmith arrangement contains an invalid value: {exception.Message}", exception);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException("The Rocksmith arrangement contains a value outside the supported range.", exception);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException($"The Rocksmith arrangement violates domain constraints: {exception.Message}", exception);
        }
    }

    /// <summary>Converts the validated XML document into one domain song and track.</summary>
    /// <param name="document">The parsed arrangement document.</param>
    /// <returns>The imported song.</returns>
    private static Song ConvertDocument(XDocument document)
    {
        var root = document.Root;
        if (root?.Name != "song")
        {
            throw new InvalidDataException("The arrangement XML must have a song root element.");
        }

        var title = RequiredElementValue(root, "title");
        var artist = root.Element("artistName")?.Value ?? string.Empty;
        if (root.Element("offset") is { } offset && ParseDecimalValue(offset.Value) != 0)
        {
            throw new InvalidDataException("Nonzero song offsets are not supported because their timeline meaning is unverified.");
        }

        var arrangement = RequiredElementValue(root, "arrangement");
        if (arrangement is not ("Lead" or "Bass" or "Rhythm"))
        {
            throw new InvalidDataException($"The Rocksmith arrangement '{arrangement}' is not supported.");
        }

        var tuning = ReadTuning(root, arrangement);
        var levels = root.Element("levels")?.Elements("level").ToArray()
            ?? throw new InvalidDataException("The arrangement has no levels element.");
        if (levels.Length == 0)
        {
            throw new InvalidDataException("The arrangement contains no difficulty levels.");
        }

        var selectedLevel = levels
            .OrderByDescending(level => ParseIntAttribute(level, "difficulty"))
            .First();
        var notes = ReadNotes(selectedLevel);
        var chords = ReadChords(selectedLevel, root, tuning.StringCount);
        return new Song(title, artist, [new Track(arrangement, tuning, notes, chords)]);
    }

    /// <summary>Reads the six XML tuning offsets and converts them to open-string MIDI pitches.</summary>
    /// <param name="root">The song root element.</param>
    /// <param name="arrangement">The supported instrument arrangement.</param>
    /// <returns>The six-string tuning.</returns>
    private static GuitarTuning ReadTuning(XElement root, string arrangement)
    {
        var tuningElement = root.Element("tuning")
            ?? throw new InvalidDataException("The arrangement has no tuning element.");
        var bassOffset = arrangement == "Bass" ? -12 : 0;
        var pitches = new int[GuitarOpenStringPitches.Length];
        for (var index = 0; index < pitches.Length; index++)
        {
            var offset = ParseIntAttribute(tuningElement, $"string{index}");
            pitches[index] = checked(GuitarOpenStringPitches[index] + bassOffset + offset);
        }

        return new GuitarTuning(pitches);
    }

    /// <summary>Reads note events from the selected difficulty level.</summary>
    /// <param name="level">The selected level element.</param>
    /// <returns>The non-ignored note events in source order.</returns>
    private static IReadOnlyList<NoteEvent> ReadNotes(XElement level)
    {
        var notesElement = level.Element("notes")
            ?? throw new InvalidDataException("The selected difficulty level has no notes element.");
        var notes = new List<NoteEvent>();
        foreach (var element in notesElement.Elements("note"))
        {
            if (ParseOptionalIntAttribute(element, "ignore", 0) != 0)
            {
                continue;
            }

            var sourceString = ParseIntAttribute(element, "string");
            var fret = ParseIntAttribute(element, "fret");
            if (sourceString is < 0 or >= 6 || fret < 0)
            {
                throw new InvalidDataException("A note has a string or fret value outside the supported range.");
            }

            var position = ParseSecondsAttribute(element, "time");
            var duration = ParseSecondsAttribute(element, "sustain");
            notes.Add(new NoteEvent(position, duration, new StringNumber(sourceString + 1), new FretNumber(fret), ReadTechniques(element)));
        }

        return notes.AsReadOnly();
    }

    /// <summary>Reads chord events using their referenced chord templates.</summary>
    /// <param name="level">The selected difficulty level.</param>
    /// <param name="root">The song root element containing chord templates.</param>
    /// <param name="stringCount">The number of strings supported by this importer.</param>
    /// <returns>Chord events in source order.</returns>
    private static IReadOnlyList<ChordEvent> ReadChords(XElement level, XElement root, int stringCount)
    {
        var templates = root.Element("chordTemplates")?.Elements("chordTemplate").ToArray()
            ?? [];
        var chordsElement = level.Element("chords")
            ?? throw new InvalidDataException("The selected difficulty level has no chords element.");
        var chords = new List<ChordEvent>();
        foreach (var element in chordsElement.Elements("chord"))
        {
            var templateIndex = ParseIntAttribute(element, "chordId");
            if ((uint)templateIndex >= (uint)templates.Length)
            {
                throw new InvalidDataException($"A chord references missing template {templateIndex}.");
            }

            var template = templates[templateIndex];
            var frettedNotes = new List<FrettedNote>();
            for (var stringIndex = 0; stringIndex < stringCount; stringIndex++)
            {
                var fret = ParseIntAttribute(template, $"fret{stringIndex}");
                if (fret < -1)
                {
                    throw new InvalidDataException($"Chord template {templateIndex} contains invalid fret {fret}.");
                }

                if (fret >= 0)
                {
                    frettedNotes.Add(new FrettedNote(new StringNumber(stringIndex + 1), new FretNumber(fret)));
                }
            }

            if (frettedNotes.Count == 0)
            {
                throw new InvalidDataException($"Chord template {templateIndex} contains no fretted strings.");
            }

            var label = (string?)template.Attribute("displayName");
            chords.Add(new ChordEvent(ParseSecondsAttribute(element, "time"), TimeSpan.Zero, frettedNotes,
                string.IsNullOrWhiteSpace(label) ? null : label));
        }

        return chords.AsReadOnly();
    }

    /// <summary>Maps supported XML note technique fields to domain flags.</summary>
    /// <param name="note">The note element.</param>
    /// <returns>The techniques present on the note.</returns>
    private static NoteTechnique ReadTechniques(XElement note)
    {
        var techniques = NoteTechnique.None;
        AddBooleanTechnique(note, "hammerOn", NoteTechnique.HammerOn, ref techniques);
        AddBooleanTechnique(note, "pullOff", NoteTechnique.PullOff, ref techniques);
        AddBooleanTechnique(note, "bend", NoteTechnique.Bend, ref techniques);
        AddBooleanTechnique(note, "slideTo", NoteTechnique.Slide, ref techniques, -1);
        AddBooleanTechnique(note, "slideUnpitchTo", NoteTechnique.Slide, ref techniques, -1);
        AddBooleanTechnique(note, "vibrato", NoteTechnique.Vibrato, ref techniques);
        AddBooleanTechnique(note, "harmonic", NoteTechnique.Harmonic, ref techniques);
        AddBooleanTechnique(note, "harmonicPinch", NoteTechnique.Harmonic, ref techniques);
        AddBooleanTechnique(note, "palmMute", NoteTechnique.PalmMute, ref techniques);
        AddBooleanTechnique(note, "accent", NoteTechnique.Accent, ref techniques);
        AddBooleanTechnique(note, "tap", NoteTechnique.Tap, ref techniques);
        AddBooleanTechnique(note, "slap", NoteTechnique.Slap, ref techniques, -1);
        AddBooleanTechnique(note, "pluck", NoteTechnique.Pluck, ref techniques, -1);
        AddBooleanTechnique(note, "tremolo", NoteTechnique.Tremolo, ref techniques);
        AddBooleanTechnique(note, "mute", NoteTechnique.Muted, ref techniques);
        return techniques;
    }

    /// <summary>Adds a technique flag when its XML attribute is set.</summary>
    /// <param name="element">The note element.</param>
    /// <param name="attributeName">The XML attribute name.</param>
    /// <param name="technique">The domain technique flag.</param>
    /// <param name="techniques">The accumulated flags.</param>
    /// <param name="inactiveValue">An optional sentinel indicating the technique is absent.</param>
    private static void AddBooleanTechnique(XElement element, string attributeName, NoteTechnique technique,
        ref NoteTechnique techniques, int inactiveValue = 0)
    {
        var value = ParseOptionalIntAttribute(element, attributeName, inactiveValue);
        if (value != inactiveValue)
        {
            techniques |= technique;
        }
    }

    /// <summary>Reads a required element's trimmed text.</summary>
    /// <param name="element">The containing XML element.</param>
    /// <param name="name">The required child element name.</param>
    /// <returns>The non-empty child value.</returns>
    private static string RequiredElementValue(XElement element, string name)
    {
        var value = element.Element(name)?.Value.Trim();
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidDataException($"The song document is missing a non-empty '{name}' element.")
            : value;
    }

    /// <summary>Parses a required integer attribute using invariant culture.</summary>
    /// <param name="element">The XML element.</param>
    /// <param name="name">The attribute name.</param>
    /// <returns>The parsed integer.</returns>
    private static int ParseIntAttribute(XElement element, string name) =>
        int.Parse((string?)element.Attribute(name) ?? throw new InvalidDataException($"An XML element is missing '{name}'."),
            NumberStyles.Integer, CultureInfo.InvariantCulture);

    /// <summary>Parses an optional integer attribute using invariant culture.</summary>
    /// <param name="element">The XML element.</param>
    /// <param name="name">The attribute name.</param>
    /// <param name="defaultValue">The value used when the attribute is absent.</param>
    /// <returns>The parsed value or default.</returns>
    private static int ParseOptionalIntAttribute(XElement element, string name, int defaultValue) =>
        element.Attribute(name) is { } attribute
            ? int.Parse(attribute.Value, NumberStyles.Integer, CultureInfo.InvariantCulture)
            : defaultValue;

    /// <summary>Parses a non-negative decimal second value into a timeline value.</summary>
    /// <param name="element">The XML element containing the attribute.</param>
    /// <param name="name">The time attribute name.</param>
    /// <returns>The parsed time.</returns>
    private static TimeSpan ParseSecondsAttribute(XElement element, string name)
    {
        var value = (string?)element.Attribute(name)
            ?? throw new InvalidDataException($"An XML element is missing '{name}'.");
        var seconds = ParseDecimalValue(value);
        if (seconds < 0)
        {
            throw new InvalidDataException($"The XML attribute '{name}' cannot be negative.");
        }

        var ticks = seconds * TimeSpan.TicksPerSecond;
        if (ticks != decimal.Truncate(ticks))
        {
            throw new InvalidDataException($"The XML attribute '{name}' has greater than 100 ns precision.");
        }

        return TimeSpan.FromTicks(decimal.ToInt64(ticks));
    }

    /// <summary>Parses a decimal XML value using invariant culture.</summary>
    /// <param name="value">The XML text value.</param>
    /// <param name="name">The field name used in diagnostics.</param>
    /// <returns>The parsed decimal value.</returns>
    private static decimal ParseDecimalValue(string value) =>
        decimal.Parse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
}
