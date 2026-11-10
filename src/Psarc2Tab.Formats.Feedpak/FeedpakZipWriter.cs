using System.IO.Compression;
using System.Text.Json;
using Psarc2Tab.Application;
using Psarc2Tab.Domain;

namespace Psarc2Tab.Formats.Feedpak;

/// <summary>Writes a feedpak-v1 ZIP package from the format-independent song model.</summary>
public sealed class FeedpakZipWriter : IFeedpakPackageWriter
{
    /// <summary>The feedpak format version emitted by this writer.</summary>
    private const string FeedpakVersion = "1.19.0";

    /// <summary>The JSON options used for stable, readable side-files.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Writes the package manifest, arrangements, timeline, lyrics, and complete WAV stem.</summary>
    /// <param name="package">The validated package content.</param>
    /// <param name="destination">The writable ZIP destination.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">The package has no arrangements or the destination is not writable.</exception>
    public void Write(FeedpakExportPackage package, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite || !destination.CanSeek)
        {
            throw new ArgumentException("The feedpak ZIP destination must be writable and seekable.", nameof(destination));
        }

        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
        var arrangements = package.Arrangements.Select((song, index) =>
        {
            var track = song.Tracks[0];
            var id = CreateArrangementId(track.Name, index);
            var file = $"arrangements/{id}.json";
            WriteJson(archive, file, writer => WriteArrangement(writer, song, track));
            return new ArrangementReference(id, track.Name, file, GetTuningOffsets(track), 0);
        }).ToArray();

        if (package.Timeline.Beats.Count > 0 || package.Timeline.Sections.Count > 0)
        {
            WriteJson(archive, "song_timeline.json", writer => WriteTimeline(writer, package.Timeline));
        }

        if (package.Lyrics is { Lyrics.Count: > 0 } lyrics)
        {
            WriteJson(archive, "lyrics.json", writer => WriteLyrics(writer, lyrics));
        }

        WriteBytes(archive, "stems/full.wav", package.AudioWave.Span);
        WriteManifest(archive, package, arrangements);
    }

    /// <summary>Creates a stable lower-case file id from an arrangement name.</summary>
    /// <param name="name">The source arrangement name.</param>
    /// <param name="index">The zero-based arrangement position.</param>
    /// <returns>A safe, unique id for one arrangement.</returns>
    private static string CreateArrangementId(string name, int index)
    {
        var characters = name.ToLowerInvariant().Select(character => char.IsAsciiLetterOrDigit(character) ? character : '-');
        var id = string.Join('-', new string(characters.ToArray()).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrEmpty(id) ? $"arrangement-{index + 1}" : $"{id}-{index + 1}";
    }

    /// <summary>Writes the top-level manifest with all files referenced explicitly.</summary>
    /// <param name="archive">The destination ZIP archive.</param>
    /// <param name="package">The song package data.</param>
    /// <param name="arrangements">The arrangement file references.</param>
    private static void WriteManifest(ZipArchive archive, FeedpakExportPackage package,
        IReadOnlyList<ArrangementReference> arrangements)
    {
        using var stream = CreateEntry(archive, "manifest.yaml");
        using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
        writer.WriteLine($"feedpak_version: {YamlString(FeedpakVersion)}");
        writer.WriteLine($"title: {YamlString(package.Song.Title)}");
        writer.WriteLine($"artist: {YamlString(package.Song.Artist)}");
        if (!string.IsNullOrWhiteSpace(package.Song.AlbumName))
        {
            writer.WriteLine($"album: {YamlString(package.Song.AlbumName)}");
        }

        if (int.TryParse(package.Song.AlbumYear, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var year))
        {
            writer.WriteLine($"year: {year}");
        }

        writer.WriteLine($"duration: {WaveFileInfo.TryRead(package.AudioWave.Span, out var waveInfo) switch
        {
            true => waveInfo.DurationSeconds.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture),
            false => throw new InvalidDataException("The feedpak audio is not a supported PCM WAV file.")
        }}");
        writer.WriteLine("arrangements:");
        foreach (var arrangement in arrangements)
        {
            writer.WriteLine($"  - id: {YamlString(arrangement.Id)}");
            writer.WriteLine($"    name: {YamlString(arrangement.Name)}");
            writer.WriteLine($"    file: {YamlString(arrangement.File)}");
            writer.WriteLine($"    tuning: [{string.Join(", ", arrangement.TuningOffsets)}]");
            writer.WriteLine($"    capo: {arrangement.Capo}");
        }

        writer.WriteLine("stems:");
        writer.WriteLine("  - id: full");
        writer.WriteLine("    file: stems/full.wav");
        writer.WriteLine("    codec: pcm");
        writer.WriteLine("    default: true");
        if (package.Lyrics is { Lyrics.Count: > 0 })
        {
            writer.WriteLine("lyrics: lyrics.json");
        }

        if (package.Timeline.Beats.Count > 0 || package.Timeline.Sections.Count > 0)
        {
            writer.WriteLine("song_timeline: song_timeline.json");
        }
    }

    /// <summary>Writes the arrangement wire format, keeping notes and chords separate.</summary>
    /// <param name="writer">The JSON writer.</param>
    /// <param name="song">The song containing timeline and pitch metadata.</param>
    /// <param name="track">The arrangement to serialize.</param>
    private static void WriteArrangement(Utf8JsonWriter writer, Song song, Track track)
    {
        writer.WriteStartObject();
        writer.WriteString("name", track.Name);
        writer.WritePropertyName("tuning");
        writer.WriteStartArray();
        foreach (var offset in GetTuningOffsets(track))
        {
            writer.WriteNumberValue(offset);
        }

        writer.WriteEndArray();
        writer.WriteNumber("capo", 0);
        writer.WriteNumber("centOffset", (double)song.CentOffsetCents);
        writer.WritePropertyName("notes");
        writer.WriteStartArray();
        foreach (var note in track.Notes.OrderBy(note => note.Position))
        {
            WriteNote(writer, note.Position, note.Duration, note.StringNumber.Value - 1,
                note.FretNumber.Value, note.Techniques, includeTime: true);
        }

        writer.WriteEndArray();
        writer.WritePropertyName("chords");
        writer.WriteStartArray();
        for (var chordIndex = 0; chordIndex < track.Chords.Count; chordIndex++)
        {
            var chord = track.Chords[chordIndex];
            writer.WriteStartObject();
            writer.WriteNumber("t", chord.Position.TotalSeconds);
            writer.WriteNumber("id", chordIndex);
            writer.WritePropertyName("notes");
            writer.WriteStartArray();
            foreach (var note in chord.Notes.OrderBy(note => note.StringNumber.Value))
            {
                WriteNote(writer, chord.Position, chord.Duration, note.StringNumber.Value - 1,
                    note.FretNumber.Value, NoteTechnique.None, includeTime: false);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WritePropertyName("templates");
        writer.WriteStartArray();
        foreach (var chord in track.Chords)
        {
            writer.WriteStartObject();
            writer.WriteString("name", chord.Label ?? string.Empty);
            writer.WritePropertyName("frets");
            writer.WriteStartArray();
            for (var stringIndex = 0; stringIndex < track.Tuning.StringCount; stringIndex++)
            {
                var note = chord.Notes.FirstOrDefault(note => note.StringNumber.Value - 1 == stringIndex);
                writer.WriteNumberValue(note.StringNumber.Value == 0 ? -1 : note.FretNumber.Value);
            }

            writer.WriteEndArray();
            writer.WritePropertyName("fingers");
            writer.WriteStartArray();
            for (var stringIndex = 0; stringIndex < track.Tuning.StringCount; stringIndex++)
            {
                writer.WriteNumberValue(-1);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    /// <summary>Writes one standalone note or one note within a chord.</summary>
    /// <param name="writer">The JSON writer.</param>
    /// <param name="position">The onset time.</param>
    /// <param name="duration">The note sustain.</param>
    /// <param name="stringIndex">The zero-based string from the lowest string.</param>
    /// <param name="fret">The fret number.</param>
    /// <param name="techniques">The note techniques.</param>
    /// <param name="includeTime">Whether the containing object has not already supplied its time.</param>
    private static void WriteNote(Utf8JsonWriter writer, TimeSpan position, TimeSpan duration, int stringIndex,
        int fret, NoteTechnique techniques, bool includeTime)
    {
        writer.WriteStartObject();
        if (includeTime)
        {
            writer.WriteNumber("t", position.TotalSeconds);
        }

        writer.WriteNumber("s", stringIndex);
        writer.WriteNumber("f", fret);
        writer.WriteNumber("sus", duration.TotalSeconds);
        WriteTechnique(writer, techniques, NoteTechnique.HammerOn, "ho");
        WriteTechnique(writer, techniques, NoteTechnique.PullOff, "po");
        WriteTechnique(writer, techniques, NoteTechnique.Harmonic, "hm");
        WriteTechnique(writer, techniques, NoteTechnique.PalmMute, "pm");
        WriteTechnique(writer, techniques, NoteTechnique.Muted, "mt");
        WriteTechnique(writer, techniques, NoteTechnique.Vibrato, "vb");
        WriteTechnique(writer, techniques, NoteTechnique.Tremolo, "tr");
        WriteTechnique(writer, techniques, NoteTechnique.Accent, "ac");
        WriteTechnique(writer, techniques, NoteTechnique.Tap, "tp");
        WriteTechnique(writer, techniques, NoteTechnique.Pluck, "plk");
        WriteTechnique(writer, techniques, NoteTechnique.Slap, "slp");
        writer.WriteEndObject();
    }

    /// <summary>Writes a technique flag only when it is present.</summary>
    /// <param name="writer">The JSON writer.</param>
    /// <param name="techniques">The combined technique flags.</param>
    /// <param name="technique">The technique being checked.</param>
    /// <param name="propertyName">The feedpak field name.</param>
    private static void WriteTechnique(Utf8JsonWriter writer, NoteTechnique techniques,
        NoteTechnique technique, string propertyName)
    {
        if ((techniques & technique) != 0)
        {
            writer.WriteBoolean(propertyName, true);
        }
    }

    /// <summary>Writes beats and sections in the song-level timeline file.</summary>
    /// <param name="writer">The JSON writer.</param>
    /// <param name="song">The source song timeline.</param>
    private static void WriteTimeline(Utf8JsonWriter writer, Song song)
    {
        writer.WriteStartObject();
        writer.WriteNumber("version", 1);
        writer.WritePropertyName("beats");
        writer.WriteStartArray();
        foreach (var beat in song.Beats)
        {
            writer.WriteStartObject();
            writer.WriteNumber("time", beat.Time.TotalSeconds);
            writer.WriteNumber("measure", beat.Beat == 0 ? beat.Measure + 1 : -1);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WritePropertyName("sections");
        writer.WriteStartArray();
        foreach (var section in song.Sections)
        {
            writer.WriteStartObject();
            writer.WriteString("name", section.Name.ToLowerInvariant());
            writer.WriteNumber("number", section.Number);
            writer.WriteNumber("time", section.StartTime.TotalSeconds);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    /// <summary>Writes timed lyric fragments as a flat feedpak lyrics array.</summary>
    /// <param name="writer">The JSON writer.</param>
    /// <param name="lyrics">The source lyrics.</param>
    private static void WriteLyrics(Utf8JsonWriter writer, SongLyrics lyrics)
    {
        writer.WriteStartArray();
        foreach (var lyric in lyrics.Lyrics)
        {
            writer.WriteStartObject();
            writer.WriteNumber("t", lyric.Position.TotalSeconds);
            if (lyric.Duration > TimeSpan.Zero)
            {
                writer.WriteNumber("d", lyric.Duration.TotalSeconds);
            }

            writer.WriteString("w", lyric.Text);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    /// <summary>Converts absolute MIDI pitches to feedpak offsets from the standard instrument tuning.</summary>
    /// <param name="track">The source arrangement.</param>
    /// <returns>Semitone offsets ordered from the lowest string.</returns>
    private static int[] GetTuningOffsets(Track track)
    {
        var standardGuitar = new[] { 40, 45, 50, 55, 59, 64 };
        var bassOffset = string.Equals(track.Name, "Bass", StringComparison.OrdinalIgnoreCase) ? -12 : 0;
        return track.Tuning.OpenStringMidiPitches.Select((pitch, index) =>
        {
            var standardPitch = (index < standardGuitar.Length ? standardGuitar[index] : 64 + (index - 5) * 5) + bassOffset;
            return pitch - standardPitch;
        }).ToArray();
    }

    /// <summary>Writes a JSON side-file into a new compressed ZIP entry.</summary>
    /// <param name="archive">The package ZIP archive.</param>
    /// <param name="name">The safe relative entry name.</param>
    /// <param name="write">The JSON serialization callback.</param>
    private static void WriteJson(ZipArchive archive, string name, Action<Utf8JsonWriter> write)
    {
        using var stream = CreateEntry(archive, name);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        write(writer);
    }

    /// <summary>Writes bytes into a new compressed ZIP entry.</summary>
    /// <param name="archive">The package ZIP archive.</param>
    /// <param name="name">The safe relative entry name.</param>
    /// <param name="bytes">The bytes to write.</param>
    private static void WriteBytes(ZipArchive archive, string name, ReadOnlySpan<byte> bytes)
    {
        using var stream = CreateEntry(archive, name);
        stream.Write(bytes);
    }

    /// <summary>Creates a deterministically timestamped compressed package entry.</summary>
    /// <param name="archive">The package ZIP archive.</param>
    /// <param name="name">The safe relative entry name.</param>
    /// <returns>The entry stream.</returns>
    private static Stream CreateEntry(ZipArchive archive, string name)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return entry.Open();
    }

    /// <summary>Encodes a string as a YAML-compatible JSON double-quoted scalar.</summary>
    /// <param name="value">The scalar text.</param>
    /// <returns>A quoted YAML scalar.</returns>
    private static string YamlString(string value) => JsonSerializer.Serialize(value);

    /// <summary>Stores a manifest arrangement reference and its tuning.</summary>
    /// <param name="Id">The stable file id.</param>
    /// <param name="Name">The display name.</param>
    /// <param name="File">The arrangement JSON path.</param>
    /// <param name="TuningOffsets">Semitone offsets from standard tuning.</param>
    /// <param name="Capo">The capo position.</param>
    private sealed record ArrangementReference(string Id, string Name, string File, int[] TuningOffsets, int Capo);
}
