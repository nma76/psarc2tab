using System.IO.Compression;
using System.Text.Json;
using Psarc2Tab.Application;
using Psarc2Tab.Domain;
using Psarc2Tab.Formats.Feedpak;

namespace Psarc2Tab.Tablature.Tests;

/// <summary>Verifies deterministic feedpak package layout and source-data mapping.</summary>
public sealed class FeedpakZipWriterTests
{
    /// <summary>Writes a package whose manifest resolves to all emitted JSON and WAV entries.</summary>
    [Fact]
    public void Write_CreatesManifestAndReferencedPackageFiles()
    {
        var package = new FeedpakExportPackage([CreateSong()], CreateWave(),
            new SongLyrics([new TimedLyric(TimeSpan.FromSeconds(1.25), "hello-", TimeSpan.FromSeconds(0.2))]));
        using var output = new MemoryStream();

        new FeedpakZipWriter().Write(package, output);

        output.Position = 0;
        using var archive = new ZipArchive(output, ZipArchiveMode.Read);
        var paths = archive.Entries.Select(entry => entry.FullName).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("manifest.yaml", paths);
        Assert.Contains("arrangements/lead-1.json", paths);
        Assert.Contains("song_timeline.json", paths);
        Assert.Contains("lyrics.json", paths);
        Assert.Contains("stems/full.wav", paths);
        using var manifestReader = new StreamReader(archive.GetEntry("manifest.yaml")!.Open());
        var manifest = manifestReader.ReadToEnd();
        Assert.Contains("feedpak_version: \"1.19.0\"", manifest, StringComparison.Ordinal);
        Assert.Contains("stems/full.wav", manifest, StringComparison.Ordinal);
        Assert.Contains("lyrics: lyrics.json", manifest, StringComparison.Ordinal);
    }

    /// <summary>Maps tuning, notes, chords, beats, sections, and lyrics into feedpak JSON.</summary>
    [Fact]
    public void Write_MapsDomainEventsToFeedpakFields()
    {
        var package = new FeedpakExportPackage([CreateSong()], CreateWave(),
            new SongLyrics([new TimedLyric(TimeSpan.FromSeconds(1.25), "hello-", TimeSpan.FromSeconds(0.2))]));
        using var output = new MemoryStream();

        new FeedpakZipWriter().Write(package, output);

        output.Position = 0;
        using var archive = new ZipArchive(output, ZipArchiveMode.Read);
        using var arrangement = JsonDocument.Parse(archive.GetEntry("arrangements/lead-1.json")!.Open());
        var root = arrangement.RootElement;
        Assert.Equal(6, root.GetProperty("tuning").GetArrayLength());
        Assert.Equal(0, root.GetProperty("tuning")[0].GetInt32());
        Assert.Equal(1, root.GetProperty("notes")[0].GetProperty("s").GetInt32());
        Assert.True(root.GetProperty("notes")[0].GetProperty("ho").GetBoolean());
        Assert.False(root.GetProperty("notes")[1].TryGetProperty("bn", out _));
        Assert.False(root.GetProperty("notes")[1].TryGetProperty("sl", out _));
        Assert.Equal(0, root.GetProperty("chords")[0].GetProperty("id").GetInt32());
        Assert.Equal(1, root.GetProperty("chords")[0].GetProperty("notes").GetArrayLength());

        using var timeline = JsonDocument.Parse(archive.GetEntry("song_timeline.json")!.Open());
        Assert.Equal(1, timeline.RootElement.GetProperty("beats")[0].GetProperty("measure").GetInt32());
        Assert.Equal("verse", timeline.RootElement.GetProperty("sections")[0].GetProperty("name").GetString());

        using var lyrics = JsonDocument.Parse(archive.GetEntry("lyrics.json")!.Open());
        Assert.Equal("hello-", lyrics.RootElement[0].GetProperty("w").GetString());
        Assert.Equal(0.2, lyrics.RootElement[0].GetProperty("d").GetDouble(), 3);
    }

    /// <summary>Rejects audio that is not a complete PCM WAV before creating an archive.</summary>
    [Fact]
    public void Package_RejectsInvalidWaveData()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new FeedpakExportPackage([CreateSong()], new byte[] { 1, 2, 3, 4 }));

        Assert.Contains("PCM WAV", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Creates a six-string guitar model with a note, chord, and timeline markers.</summary>
    /// <returns>A domain song fixture.</returns>
    private static Song CreateSong()
    {
        var tuning = new GuitarTuning([40, 45, 50, 55, 59, 64]);
        var note = new NoteEvent(TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(0.25),
            new StringNumber(2), new FretNumber(3), NoteTechnique.HammerOn);
        var chord = new ChordEvent(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(0.5),
            [new FrettedNote(new StringNumber(1), new FretNumber(0))], "E");
        var unknownDetail = new NoteEvent(TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(0.2),
            new StringNumber(3), new FretNumber(5), NoteTechnique.Bend | NoteTechnique.Slide);
        var track = new Track("Lead", tuning, [note, unknownDetail], [chord]);
        return new Song("Song: One", "Artist", [track], [new SongSection("Verse", 1, TimeSpan.FromSeconds(2))],
            beats: [new BeatMarker(TimeSpan.Zero, 0, 0)]);
    }

    /// <summary>Creates a short 16-bit stereo PCM WAV fixture with valid RIFF chunk sizes.</summary>
    /// <returns>The WAV bytes.</returns>
    private static byte[] CreateWave()
    {
        const int sampleRate = 8_000;
        const short channels = 2;
        const short bitsPerSample = 16;
        const int dataLength = 32;
        var bytes = new byte[44 + dataLength];
        "RIFF"u8.CopyTo(bytes);
        BitConverter.GetBytes(bytes.Length - 8).CopyTo(bytes, 4);
        "WAVE"u8.CopyTo(bytes.AsSpan(8));
        "fmt "u8.CopyTo(bytes.AsSpan(12));
        BitConverter.GetBytes(16).CopyTo(bytes, 16);
        BitConverter.GetBytes((short)1).CopyTo(bytes, 20);
        BitConverter.GetBytes(channels).CopyTo(bytes, 22);
        BitConverter.GetBytes(sampleRate).CopyTo(bytes, 24);
        BitConverter.GetBytes(sampleRate * channels * bitsPerSample / 8).CopyTo(bytes, 28);
        BitConverter.GetBytes((short)(channels * bitsPerSample / 8)).CopyTo(bytes, 32);
        BitConverter.GetBytes(bitsPerSample).CopyTo(bytes, 34);
        "data"u8.CopyTo(bytes.AsSpan(36));
        BitConverter.GetBytes(dataLength).CopyTo(bytes, 40);
        return bytes;
    }
}
