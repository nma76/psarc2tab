using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Psarc2Tab.Domain;

namespace Psarc2Tab.Formats.Rocksmith;

/// <summary>Reads timed lyric fragments from a Rocksmith vocals XML document.</summary>
public sealed class RocksmithLyricsXmlImporter
{
    /// <summary>Imports vocal entries from a readable XML stream.</summary>
    /// <param name="source">The XML stream.</param>
    /// <returns>The timed lyrics in document order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="source"/> is not readable.</exception>
    /// <exception cref="InvalidDataException">The XML or its lyric entries are malformed.</exception>
    public SongLyrics Import(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
        {
            throw new ArgumentException("The vocals XML stream must be readable.", nameof(source));
        }

        try
        {
            using var reader = XmlReader.Create(source, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 64L * 1024 * 1024
            });
            var document = XDocument.Load(reader);
            var root = document.Root;
            if (root is null || root.Name.LocalName != "vocals")
            {
                throw new InvalidDataException("The Rocksmith vocals XML must have a vocals root element.");
            }

            var lyrics = root.Elements().Where(element => element.Name.LocalName == "vocal")
                .Select(ReadLyric).ToArray();
            return new SongLyrics(lyrics);
        }
        catch (XmlException exception)
        {
            throw new InvalidDataException($"The Rocksmith vocals XML is malformed: {exception.Message}", exception);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidDataException("The Rocksmith vocals XML contains lyric timing outside the supported range.", exception);
        }
    }

    /// <summary>Reads the timing and text from one vocal element.</summary>
    /// <param name="element">The vocal XML element.</param>
    /// <returns>The imported lyric fragment.</returns>
    /// <exception cref="InvalidDataException">A required attribute is missing or invalid.</exception>
    private static TimedLyric ReadLyric(XElement element)
    {
        var timeText = (string?)element.Attribute("time");
        var lengthText = (string?)element.Attribute("length");
        var text = (string?)element.Attribute("lyric");
        if (!float.TryParse(timeText, NumberStyles.Float, CultureInfo.InvariantCulture, out var time) ||
            !float.TryParse(lengthText, NumberStyles.Float, CultureInfo.InvariantCulture, out var length) ||
            !float.IsFinite(time) || time < 0 || !float.IsFinite(length) || length < 0 ||
            string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidDataException("A Rocksmith vocal entry has missing or invalid time, length, or lyric text.");
        }

        return new TimedLyric(TimeSpan.FromSeconds(time), text);
    }
}
