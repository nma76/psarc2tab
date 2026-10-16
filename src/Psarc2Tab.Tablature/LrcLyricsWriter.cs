using System.Globalization;
using System.Text;
using Psarc2Tab.Application;
using Psarc2Tab.Domain;

namespace Psarc2Tab.Tablature;

/// <summary>Writes timed lyrics using the plain-text LRC line timestamp format.</summary>
public sealed class LrcLyricsWriter : ILyricsDocumentWriter
{
    /// <summary>The maximum preferred line length when source lyrics have no line marker.</summary>
    private const int MaximumLineLength = 80;

    /// <summary>Writes timed lyrics as UTF-8-compatible LRC text.</summary>
    /// <param name="lyrics">The lyric fragments in source order.</param>
    /// <param name="destination">The destination text writer.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="InvalidOperationException">The lyrics contain no non-empty text.</exception>
    public void Write(SongLyrics lyrics, TextWriter destination)
    {
        ArgumentNullException.ThrowIfNull(lyrics);
        ArgumentNullException.ThrowIfNull(destination);

        var fragments = lyrics.Lyrics
            .Where(lyric => !string.IsNullOrWhiteSpace(lyric.Text))
            .OrderBy(lyric => lyric.Position)
            .ToArray();
        if (fragments.Length == 0)
        {
            throw new InvalidOperationException("The archive contains no lyric text to export.");
        }

        var line = new StringBuilder();
        var linePosition = TimeSpan.Zero;
        foreach (var fragment in fragments)
        {
            var text = fragment.Text.Trim();
            var endsLine = text.EndsWith('+');
            if (endsLine)
            {
                text = text[..^1].TrimEnd();
            }

            if (text.Length == 0)
            {
                FlushLine(line, linePosition, destination);
                continue;
            }

            var separator = line.Length > 0 && !line.ToString().EndsWith('-') && !text.StartsWith('-') ? " " : string.Empty;
            if (line.Length > 0 && line.Length + separator.Length + text.Length > MaximumLineLength)
            {
                FlushLine(line, linePosition, destination);
                separator = string.Empty;
            }

            if (line.Length == 0)
            {
                linePosition = fragment.Position;
            }

            line.Append(separator).Append(text);
            if (endsLine)
            {
                FlushLine(line, linePosition, destination);
            }
        }

        FlushLine(line, linePosition, destination);
    }

    /// <summary>Writes and clears one accumulated lyric line.</summary>
    /// <param name="line">The current line buffer.</param>
    /// <param name="position">The first fragment's start time.</param>
    /// <param name="destination">The output writer.</param>
    private static void FlushLine(StringBuilder line, TimeSpan position, TextWriter destination)
    {
        if (line.Length == 0)
        {
            return;
        }

        destination.Write('[');
        destination.Write(FormatTimestamp(position));
        destination.Write(']');
        destination.Write(line.ToString());
        destination.Write('\n');
        line.Clear();
    }

    /// <summary>Formats a non-negative song position as an LRC timestamp.</summary>
    /// <param name="position">The position to format.</param>
    /// <returns>The timestamp in minutes, seconds, and hundredths.</returns>
    private static string FormatTimestamp(TimeSpan position)
    {
        var totalHundredths = (long)Math.Round(position.Ticks / (double)TimeSpan.TicksPerMillisecond / 10,
            MidpointRounding.AwayFromZero);
        var minutes = totalHundredths / 6000;
        var seconds = totalHundredths / 100 % 60;
        var hundredths = totalHundredths % 100;
        return string.Create(CultureInfo.InvariantCulture, $"{minutes:00}:{seconds:00}.{hundredths:00}");
    }
}
