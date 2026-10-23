using System.Globalization;
using System.Text;
using Psarc2Tab.Application;
using Psarc2Tab.Domain;

namespace Psarc2Tab.Tablature;

/// <summary>Writes a shaded text heatmap with fixed count bands for arrangement comparison.</summary>
public sealed class FretboardHeatmapTextWriter : IArrangementFormatWriter
{
    /// <summary>Sharp note names indexed by MIDI pitch class.</summary>
    private static readonly string[] PitchNames = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    /// <summary>Gets the command-line name of the heatmap format.</summary>
    public string FormatName => "heatmap";

    /// <summary>Writes string/fret use counts as a compact ASCII intensity grid.</summary>
    /// <param name="song">The song that owns the track.</param>
    /// <param name="track">The selected arrangement track.</param>
    /// <param name="destination">The writable destination stream.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="InvalidDataException">An event uses a fret above the heatmap display limit.</exception>
    public void Write(Song song, Track track, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(song);
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(destination);
        var heatmap = FretboardHeatmapCalculator.Calculate(track);
        using var writer = new StreamWriter(destination, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 1024, leaveOpen: true);

        writer.WriteLine("Fretboard heatmap (beta)");
        writer.WriteLine($"Arrangement: {track.Name}");
        writer.WriteLine("Strings are shown from highest to lowest pitch; string numbers follow the tuning order.");
        writer.WriteLine("Counts include individual notes and each tone in a chord.");
        writer.WriteLine($"Tuning (low to high): {FormatTuning(track.Tuning)}");
        writer.WriteLine();
        WriteGrid(writer, track, heatmap);
        writer.WriteLine();
        writer.WriteLine("Uses per position: blank=0, ░=1-15, ▒=16-63, ▓=64-127, █=128+");
        writer.WriteLine($"Maximum uses at one position: {heatmap.MaximumUseCount}");
        writer.Flush();
    }

    /// <summary>Writes fret numbers and one usage-symbol row per tuned string.</summary>
    /// <param name="writer">The output text writer.</param>
    /// <param name="track">The track providing tuning and string labels.</param>
    /// <param name="heatmap">The calculated position-use counts.</param>
    private static void WriteGrid(TextWriter writer, Track track, FretboardHeatmap heatmap)
    {
        var labels = Enumerable.Range(1, track.Tuning.StringCount)
            .Select(stringNumber => FormatStringLabel(track.Tuning.OpenStringMidiPitches[stringNumber - 1], stringNumber))
            .ToArray();
        var labelWidth = Math.Max(8, labels.Max(label => label.Length) + 1);
        writer.Write(new string(' ', labelWidth));
        for (var fret = 0; fret <= heatmap.MaximumFret; fret++)
        {
            writer.Write(fret.ToString(CultureInfo.InvariantCulture).PadLeft(3));
        }

        writer.WriteLine();
        for (var stringNumber = track.Tuning.StringCount; stringNumber >= 1; stringNumber--)
        {
            writer.Write(labels[stringNumber - 1].PadRight(labelWidth));
            for (var fret = 0; fret <= heatmap.MaximumFret; fret++)
            {
                writer.Write(FormatUse(heatmap.GetUseCount(new StringNumber(stringNumber), new FretNumber(fret)))
                    .ToString().PadLeft(3));
            }

            writer.WriteLine();
        }
    }

    /// <summary>Maps an absolute use count to the documented fixed shading symbol.</summary>
    /// <param name="useCount">The nonnegative count at one position.</param>
    /// <returns>A printable Unicode block-shading symbol.</returns>
    private static char FormatUse(int useCount) => useCount switch
    {
        0 => ' ',
        <= 15 => '░',
        <= 63 => '▒',
        <= 127 => '▓',
        _ => '█'
    };

    /// <summary>Formats an open-string pitch and its one-based string number.</summary>
    /// <param name="midiPitch">The open-string MIDI pitch.</param>
    /// <param name="stringNumber">The one-based string number.</param>
    /// <returns>A short ASCII row label.</returns>
    private static string FormatStringLabel(int midiPitch, int stringNumber) =>
        $"{stringNumber.ToString(CultureInfo.InvariantCulture)} {PitchNames[midiPitch % PitchNames.Length]}{midiPitch / 12 - 1}";

    /// <summary>Formats open-string pitches from the lowest to highest string.</summary>
    /// <param name="tuning">The track tuning.</param>
    /// <returns>A space-separated ASCII tuning.</returns>
    private static string FormatTuning(GuitarTuning tuning) => string.Join(" ", tuning.OpenStringMidiPitches.Select(pitch =>
        $"{PitchNames[pitch % PitchNames.Length]}{pitch / 12 - 1}"));
}
