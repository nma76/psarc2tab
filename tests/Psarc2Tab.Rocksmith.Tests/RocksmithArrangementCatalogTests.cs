using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Psarc2Tab.Application;
using Psarc2Tab.Domain;
using Psarc2Tab.Formats.Rocksmith;

namespace Psarc2Tab.Rocksmith.Tests;

/// <summary>Verifies arrangement discovery from a PSARC manifest.</summary>
public sealed class RocksmithArrangementCatalogTests
{
    /// <summary>Explains that the selected decoder must have Vorbis support when a Wwise Vorbis stream fails.</summary>
    [Fact]
    public void VgmstreamDecoder_WwiseVorbisFailure_ReportsCodecRequirement()
    {
        var wem = new byte[22];
        "RIFF"u8.CopyTo(wem);
        "WAVE"u8.CopyTo(wem.AsSpan(8));
        "fmt "u8.CopyTo(wem.AsSpan(12));
        wem[20] = 0xFF;
        wem[21] = 0xFF;
        var decoder = new VgmstreamWwiseAudioDecoder(new RejectingVgmstreamRunner());

        var exception = Assert.Throws<InvalidOperationException>(() => decoder.DecodeToWave(wem));

        Assert.Contains("Vorbis/libvorbis support", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Accepts arrangement-specific timeline differences and uses the most complete song timeline.</summary>
    [Fact]
    public void FeedpakImporter_DifferentArrangementTimelines_CombinesRichestBeatAndSectionData()
    {
        var lead = CreateSong("Lead", [
            new BeatMarker(TimeSpan.Zero, 0, 0),
            new BeatMarker(TimeSpan.FromSeconds(0.5), 0, 1)
        ], []);
        var bass = CreateSong("Bass", [
            new BeatMarker(TimeSpan.Zero, 0, 0),
            new BeatMarker(TimeSpan.FromSeconds(0.5), 0, 1),
            new BeatMarker(TimeSpan.FromSeconds(1), 0, 2)
        ], [new SongSection("Verse", 1, TimeSpan.Zero)]);
        var catalog = new RocksmithArrangementCatalog();
        var importer = new RocksmithArchiveFeedpakImporter(catalog,
            new TestArrangementImporter([lead, bass]), new TestLyricsImporter(),
            new RocksmithArchiveAudioImporter(catalog, new TestWwiseAudioDecoder()));
        var paths = new[]
        {
            "songs/arr/sample_lead.xml",
            "songs/arr/sample_bass.xml",
            "audio/windows/song.wem"
        };
        var payloads = new Dictionary<string, byte[]> { [paths[2]] = [1, 2, 3, 4] };
        using var archive = new MemoryStream(CreateArchive(paths, payloads: payloads));

        var package = importer.Import(archive);

        Assert.Equal(2, package.Arrangements.Count);
        Assert.Equal(3, package.Timeline.Beats.Count);
        Assert.Single(package.Timeline.Sections);
    }

    /// <summary>Lists supported instrument arrangements and skips vocals and show-light data.</summary>
    [Fact]
    public void List_MultipleArrangements_ReturnsNumberedInstrumentsInManifestOrder()
    {
        var archive = CreateArchive(
        [
            "songs/arr/sample_vocals.xml",
            "songs/arr/sample_bass.xml",
            "songs/arr/sample_lead.xml",
            "songs/arr/sample_showlights.xml",
            "songs/arr/sample_rhythm.xml"
        ]);
        using var stream = new MemoryStream(archive);

        var arrangements = new RocksmithArrangementCatalog().List(stream);

        Assert.Collection(
            arrangements,
            arrangement => Assert.Equal(new RocksmithArrangement(1, "Bass", "songs/arr/sample_bass.xml"), arrangement),
            arrangement => Assert.Equal(new RocksmithArrangement(2, "Lead", "songs/arr/sample_lead.xml"), arrangement),
            arrangement => Assert.Equal(new RocksmithArrangement(3, "Rhythm", "songs/arr/sample_rhythm.xml"), arrangement));
    }

    /// <summary>Inspects entry lengths and extracts a requested path from the PSARC manifest.</summary>
    [Fact]
    public void ExtractEntry_ReturnsMatchingPathAndPayload()
    {
        const string chartPath = "songs/arr/sample_lead.xml";
        const string audioPath = "audio/windows/123.wem";
        byte[] audio = [0x52, 0x49, 0x46, 0x46, 0x01, 0x02];
        var archiveBytes = CreateArchive([chartPath, audioPath], payloads: new Dictionary<string, byte[]>
        {
            [audioPath] = audio
        });
        var catalog = new RocksmithArrangementCatalog();
        using var archive = new MemoryStream(archiveBytes);

        var details = catalog.Inspect(archive);

        Assert.Equal((ulong)audio.Length,
            Assert.Single(details.Entries, entry => entry.Path == audioPath).UncompressedLength);
        archive.Position = 0;
        using var extracted = new MemoryStream();
        var entry = catalog.ExtractEntry(archive, audioPath, extracted);

        Assert.Equal(audioPath, entry.Path);
        Assert.Equal(audio, extracted.ToArray());
    }

    /// <summary>Selects the much larger WEM file as the song mix instead of the preview.</summary>
    [Fact]
    public void ArchiveAudioImporter_SelectsLargerMixAndDecodesIt()
    {
        const string chartPath = "songs/arr/sample_lead.xml";
        const string mixPath = "audio/windows/123.wem";
        const string previewPath = "audio/windows/456.wem";
        byte[] mix = Enumerable.Repeat((byte)0x31, 12).ToArray();
        byte[] preview = [0x50, 0x52, 0x45];
        var archiveBytes = CreateArchive([chartPath, mixPath, previewPath], payloads: new Dictionary<string, byte[]>
        {
            [mixPath] = mix,
            [previewPath] = preview
        });
        using var archive = new MemoryStream(archiveBytes);
        var decoder = new RecordingWwiseAudioDecoder();
        var importer = new RocksmithArchiveAudioImporter(new RocksmithArrangementCatalog(), decoder);

        var result = importer.Import(archive);

        Assert.Equal(mix, decoder.LastInput);
        Assert.Equal([0x52, 0x49, 0x46, 0x46], result);
    }

    /// <summary>Rejects WEM candidates whose relative sizes do not identify a likely preview.</summary>
    [Fact]
    public void ArchiveAudioImporter_AmbiguousWemSizes_ThrowsInvalidDataException()
    {
        var archiveBytes = CreateArchive(
            ["songs/arr/sample_lead.xml", "audio/windows/123.wem", "audio/windows/456.wem"],
            payloads: new Dictionary<string, byte[]>
            {
                ["audio/windows/123.wem"] = [1, 2, 3, 4],
                ["audio/windows/456.wem"] = [5, 6, 7]
            });
        using var archive = new MemoryStream(archiveBytes);
        var importer = new RocksmithArchiveAudioImporter(new RocksmithArrangementCatalog(),
            new RecordingWwiseAudioDecoder());

        Assert.Throws<InvalidDataException>(() => importer.Import(archive));
    }

    /// <summary>Appends a matching vocals XML file as the final selectable item.</summary>
    [Fact]
    public void ListItems_VocalsXml_AppendsLyricsAfterArrangements()
    {
        var archive = CreateArchive(
        [
            "songs/arr/sample_vocals.xml",
            "songs/arr/sample_lead.xml",
            "songs/arr/sample_bass.xml"
        ]);
        using var stream = new MemoryStream(archive);

        var items = new RocksmithArrangementCatalog().ListItems(stream);

        Assert.Collection(items,
            item => { Assert.Equal(1, item.Number); Assert.Equal("Lead", item.Name); Assert.False(item.IsLyrics); },
            item => { Assert.Equal(2, item.Number); Assert.Equal("Bass", item.Name); Assert.False(item.IsLyrics); },
            item => { Assert.Equal(3, item.Number); Assert.Equal("Lyrics", item.Name); Assert.True(item.IsLyrics); });
    }

    /// <summary>Rejects a manifest whose path count differs from the archive index.</summary>
    [Fact]
    public void List_MismatchedManifestCount_ThrowsInvalidDataException()
    {
        var archive = CreateArchive(["songs/arr/sample_lead.xml"], manifestPaths: []);
        using var stream = new MemoryStream(archive);

        Assert.Throws<InvalidDataException>(() => new RocksmithArrangementCatalog().List(stream));
    }

    /// <summary>Rejects a manifest path that does not match its TOC digest.</summary>
    [Fact]
    public void List_MismatchedPathDigest_ThrowsInvalidDataException()
    {
        var archive = CreateArchive(
            ["songs/arr/sample_lead.xml"],
            manifestPaths: ["songs/arr/other_lead.xml"]);
        using var stream = new MemoryStream(archive);

        Assert.Throws<InvalidDataException>(() => new RocksmithArrangementCatalog().List(stream));
    }

    /// <summary>Discovers an SNG chart through its matching JSON arrangement manifest.</summary>
    [Fact]
    public void List_SngPackage_ReturnsArrangementAndChartPath()
    {
        const string jsonPath = "manifests/songs_dlc_example/example_bass.json";
        const string chartPath = "songs/bin/generic/example_bass.sng";
        var metadata = """{"Entries":{"id":{"Attributes":{"ArrangementName":"Bass","SongXml":"urn:application:xml:example_bass","SongName":"Example Song","ArtistName":"Example Artist","AlbumName":"Example Album","AlbumYear":1998,"CentOffset":-33.5,"Tuning":{"string0":0,"string1":0,"string2":0,"string3":0,"string4":0,"string5":0}}}}}""";
        var chart = new byte[] { 0x4A, 0x00, 0x00, 0x00 };
        var archive = CreateArchive([chartPath, jsonPath], payloads: new Dictionary<string, byte[]>
        {
            [chartPath] = chart,
            [jsonPath] = Encoding.UTF8.GetBytes(metadata)
        });
        using var stream = new MemoryStream(archive);

        var arrangements = new RocksmithArrangementCatalog().List(stream);

        var arrangement = Assert.Single(arrangements);
        Assert.Equal("Bass", arrangement.Instrument);
        Assert.Equal(chartPath, arrangement.ArchivePath);
        Assert.Equal(jsonPath, arrangement.MetadataPath);
        Assert.Equal(RocksmithArrangementFormat.Sng, arrangement.DataFormat);
        Assert.Equal("Example Song", arrangement.SngMetadata?.Title);
        Assert.Equal("Example Artist", arrangement.SngMetadata?.Artist);
        Assert.Equal("Example Album", arrangement.SngMetadata?.AlbumName);
        Assert.Equal("1998", arrangement.SngMetadata?.AlbumYear);
        Assert.Equal(new[] { 0, 0, 0, 0, 0, 0 }, arrangement.SngMetadata?.TuningOffsets);
        Assert.Equal(-33.5m, arrangement.SngMetadata?.CentOffsetCents);
    }

    /// <summary>Finds the vocal SNG associated with a manifest-discovered arrangement.</summary>
    [Fact]
    public void ListItems_SngPackage_AppendsMatchingLyricsChart()
    {
        const string jsonPath = "manifests/songs_dlc_example/example_bass.json";
        const string chartPath = "songs/bin/generic/example_bass.sng";
        const string lyricsPath = "songs/bin/generic/example_vocals.sng";
        var metadata = """{"Entries":{"id":{"Attributes":{"ArrangementName":"Bass","SongXml":"urn:application:xml:example_bass","SongName":"Example Song","ArtistName":"Example Artist","Tuning":{"string0":0,"string1":0,"string2":0,"string3":0,"string4":0,"string5":0}}}}}""";
        var archive = CreateArchive([chartPath, lyricsPath, jsonPath], payloads: new Dictionary<string, byte[]>
        {
            [chartPath] = [0x4A, 0x00, 0x00, 0x00],
            [lyricsPath] = [0x4A, 0x00, 0x00, 0x00],
            [jsonPath] = Encoding.UTF8.GetBytes(metadata)
        });
        using var stream = new MemoryStream(archive);

        var items = new RocksmithArrangementCatalog().ListItems(stream);

        Assert.Collection(items,
            item => Assert.Equal("Bass", item.Name),
            item =>
            {
                Assert.Equal("Lyrics", item.Name);
                Assert.Equal(lyricsPath, item.ArchivePath);
                Assert.True(item.IsLyrics);
            });
    }

    /// <summary>Extracts the chart payload rather than its JSON metadata.</summary>
    [Fact]
    public void Extract_SngPackage_ReturnsChartBytes()
    {
        const string jsonPath = "manifests/songs_dlc_example/example_bass.json";
        const string chartPath = "songs/bin/generic/example_bass.sng";
        var metadata = """{"Entries":{"id":{"Attributes":{"ArrangementName":"Bass","SongXml":"urn:application:xml:example_bass","SongName":"Example Song","ArtistName":"Example Artist","Tuning":{"string0":0,"string1":0,"string2":0,"string3":0,"string4":0,"string5":0}}}}}""";
        var chart = new byte[] { 0x4A, 0x00, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00 };
        var archive = CreateArchive([chartPath, jsonPath], payloads: new Dictionary<string, byte[]>
        {
            [chartPath] = chart,
            [jsonPath] = Encoding.UTF8.GetBytes(metadata)
        });
        using var stream = new MemoryStream(archive);
        using var destination = new MemoryStream();

        var arrangement = new RocksmithArrangementCatalog().Extract(stream, 1, destination);

        Assert.Equal(RocksmithArrangementFormat.Sng, arrangement.DataFormat);
        Assert.Equal(chart, destination.ToArray());
    }

    /// <summary>Rejects malformed SNG chart containers with a useful error.</summary>
    [Fact]
    public void Import_SngPackage_RejectsMalformedContainer()
    {
        const string jsonPath = "manifests/songs_dlc_example/example_bass.json";
        const string chartPath = "songs/bin/generic/example_bass.sng";
        var metadata = """{"Entries":{"id":{"Attributes":{"ArrangementName":"Bass","SongXml":"urn:application:xml:example_bass","SongName":"Example Song","ArtistName":"Example Artist","Tuning":{"string0":0,"string1":0,"string2":0,"string3":0,"string4":0,"string5":0}}}}}""";
        var archive = CreateArchive([chartPath, jsonPath], payloads: new Dictionary<string, byte[]>
        {
            [chartPath] = [0x4A, 0x00, 0x00, 0x00],
            [jsonPath] = Encoding.UTF8.GetBytes(metadata)
        });
        using var stream = new MemoryStream(archive);

        var exception = Assert.Throws<InvalidDataException>(() =>
            new RocksmithArchiveArrangementImporter(new RocksmithArrangementCatalog(), new RocksmithArrangementImporter())
                .Import(stream, 1));

        Assert.Contains("SNG container", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Prefers XML when an archive includes both XML and SNG for one arrangement.</summary>
    [Fact]
    public void List_DualFormatPackage_ListsXmlArrangementOnly()
    {
        const string xmlPath = "songs/arr/example_bass.xml";
        const string jsonPath = "manifests/songs_dlc_example/example_bass.json";
        const string chartPath = "songs/bin/generic/example_bass.sng";
        var metadata = """{"Entries":{"id":{"Attributes":{"ArrangementName":"Bass","SongXml":"urn:application:xml:example_bass","SongName":"Example Song","ArtistName":"Example Artist","Tuning":{"string0":0,"string1":0,"string2":0,"string3":0,"string4":0,"string5":0}}}}}""";
        var archive = CreateArchive([xmlPath, chartPath, jsonPath], payloads: new Dictionary<string, byte[]>
        {
            [xmlPath] = "<song />"u8.ToArray(),
            [chartPath] = [0x4A, 0x00, 0x00, 0x00],
            [jsonPath] = Encoding.UTF8.GetBytes(metadata)
        });
        using var stream = new MemoryStream(archive);

        var arrangement = Assert.Single(new RocksmithArrangementCatalog().List(stream));

        Assert.Equal(xmlPath, arrangement.ArchivePath);
        Assert.Equal(RocksmithArrangementFormat.Xml, arrangement.DataFormat);
    }

    /// <summary>Imports an internal real-guitar XML part when its archive path identifies Rhythm.</summary>
    [Fact]
    public void Import_RhythmArchiveEntryWithInternalPartName_UsesArchiveContext()
    {
        const string rhythmPath = "songs/arr/remmotm_rhythm.xml";
        const string xml = """
            <song><title>Example Song</title><artistName>Example Artist</artistName>
              <arrangement>PART REAL_GUITAR_22</arrangement>
              <tuning string0="0" string1="0" string2="0" string3="0" string4="0" string5="0" />
              <levels><level difficulty="0"><notes /><chords /></level></levels>
            </song>
            """;
        var archive = CreateArchive([rhythmPath], payloads: new Dictionary<string, byte[]>
        {
            [rhythmPath] = Encoding.UTF8.GetBytes(xml)
        });
        using var stream = new MemoryStream(archive);

        var song = new RocksmithArchiveArrangementImporter(
            new RocksmithArrangementCatalog(), new RocksmithArrangementImporter()).Import(stream, 1);

        Assert.Equal("Rhythm", Assert.Single(song.Tracks).Name);
        Assert.Empty(Assert.Single(song.Tracks).Chords);
    }

    /// <summary>Builds an archive with a manifest and one raw block for each path.</summary>
    /// <param name="paths">The non-manifest archive paths.</param>
    /// <param name="manifestPaths">Optional paths to encode in the manifest.</param>
    /// <param name="payloads">Optional payloads keyed by archive path.</param>
    /// <returns>A minimal unencrypted PSARC byte sequence.</returns>
    private static byte[] CreateArchive(string[] paths, string[]? manifestPaths = null,
        Dictionary<string, byte[]>? payloads = null)
    {
        const int headerSize = 32;
        const int entrySize = 30;
        var manifest = Encoding.UTF8.GetBytes(string.Join('\n', manifestPaths ?? paths));
        var content = new[] { manifest }.Concat(paths.Select(path =>
            payloads is not null && payloads.TryGetValue(path, out var payload) ? payload : "<song />"u8.ToArray())).ToArray();
        var tocSize = headerSize + content.Length * (entrySize + sizeof(ushort));
        var dataSize = content.Sum(entry => entry.Length);
        var archive = new byte[tocSize + dataSize];

        "PSAR"u8.CopyTo(archive);
        BinaryPrimitives.WriteUInt32BigEndian(archive.AsSpan(4), 0x0001_0004);
        "zlib"u8.CopyTo(archive.AsSpan(8));
        BinaryPrimitives.WriteUInt32BigEndian(archive.AsSpan(12), (uint)tocSize);
        BinaryPrimitives.WriteUInt32BigEndian(archive.AsSpan(16), entrySize);
        BinaryPrimitives.WriteUInt32BigEndian(archive.AsSpan(20), (uint)content.Length);
        BinaryPrimitives.WriteUInt32BigEndian(archive.AsSpan(24), 65_536);

        var blockTableOffset = headerSize + content.Length * entrySize;
        var dataOffset = tocSize;
        for (var entryIndex = 0; entryIndex < content.Length; entryIndex++)
        {
            var recordOffset = headerSize + entryIndex * entrySize;
            if (entryIndex > 0)
            {
                MD5.HashData(Encoding.UTF8.GetBytes(paths[entryIndex - 1]))
                    .CopyTo(archive.AsSpan(recordOffset, 16));
            }

            BinaryPrimitives.WriteUInt32BigEndian(archive.AsSpan(recordOffset + 16), (uint)entryIndex);
            WriteUInt40(archive.AsSpan(recordOffset + 20), (ulong)content[entryIndex].Length);
            WriteUInt40(archive.AsSpan(recordOffset + 25), (ulong)dataOffset);
            content[entryIndex].CopyTo(archive, dataOffset);
            dataOffset += content[entryIndex].Length;
            BinaryPrimitives.WriteUInt16BigEndian(archive.AsSpan(blockTableOffset + entryIndex * 2), 0);
        }

        return archive;
    }

    /// <summary>Creates a song fixture with the requested arrangement and timeline.</summary>
    /// <param name="name">The instrument arrangement name.</param>
    /// <param name="beats">The beat markers.</param>
    /// <param name="sections">The section markers.</param>
    /// <returns>A single-track song fixture.</returns>
    private static Song CreateSong(string name, IReadOnlyList<BeatMarker> beats, IReadOnlyList<SongSection> sections)
    {
        var track = new Track(name, new GuitarTuning([40, 45, 50, 55, 59, 64]), [], []);
        return new Song("Fixture Song", "Fixture Artist", [track], sections, beats: beats);
    }

    /// <summary>Returns songs from a fixed archive arrangement listing.</summary>
    private sealed class TestArrangementImporter : IArchiveArrangementImporter
    {
        /// <summary>The songs returned for one-based arrangement numbers.</summary>
        private readonly IReadOnlyList<Song> _songs;

        /// <summary>Creates the fake importer.</summary>
        /// <param name="songs">The arrangement songs.</param>
        public TestArrangementImporter(IReadOnlyList<Song> songs) => _songs = songs;

        /// <inheritdoc />
        public Song Import(Stream archive, int arrangementNumber) => _songs[arrangementNumber - 1];
    }

    /// <summary>Fails if the archive unexpectedly requests lyrics.</summary>
    private sealed class TestLyricsImporter : IArchiveLyricsImporter
    {
        /// <inheritdoc />
        public SongLyrics Import(Stream archive) => throw new InvalidOperationException("No lyrics were expected.");
    }

    /// <summary>Returns a minimal RIFF/WAVE header for package assembly.</summary>
    private sealed class TestWwiseAudioDecoder : IWwiseAudioDecoder
    {
        /// <inheritdoc />
        public byte[] DecodeToWave(ReadOnlyMemory<byte> wemData)
        {
            var wave = new byte[12];
            "RIFF"u8.CopyTo(wave);
            "WAVE"u8.CopyTo(wave.AsSpan(8));
            return wave;
        }
    }

    /// <summary>Simulates vgmstream rejecting a Wwise Vorbis input.</summary>
    private sealed class RejectingVgmstreamRunner : IVgmstreamRunner
    {
        /// <inheritdoc />
        public void Decode(string inputPath, string outputPath) =>
            throw new InvalidDataException("vgmstream could not decode the WEM audio (exit code 1). failed opening input.");
    }

    /// <summary>Writes an unsigned 40-bit integer in network byte order.</summary>
    /// <param name="destination">The five-byte destination field.</param>
    /// <param name="value">The value to encode.</param>
    private static void WriteUInt40(Span<byte> destination, ulong value)
    {
        for (var index = 4; index >= 0; index--)
        {
            destination[index] = (byte)value;
            value >>= 8;
        }
    }

    /// <summary>Records the payload passed to a fake Wwise decoder in tests.</summary>
    private sealed class RecordingWwiseAudioDecoder : IWwiseAudioDecoder
    {
        /// <summary>Gets the most recently decoded WEM bytes.</summary>
        public byte[] LastInput { get; private set; } = [];

        /// <summary>Returns a small marker in place of decoded WAV data.</summary>
        /// <param name="wemData">The selected WEM payload.</param>
        /// <returns>A synthetic WAV marker.</returns>
        public byte[] DecodeToWave(ReadOnlyMemory<byte> wemData)
        {
            LastInput = wemData.ToArray();
            return [0x52, 0x49, 0x46, 0x46];
        }
    }
}
