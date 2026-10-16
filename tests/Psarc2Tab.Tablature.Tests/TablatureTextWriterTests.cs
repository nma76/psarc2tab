using Psarc2Tab.Domain;
using Psarc2Tab.Tablature;

namespace Psarc2Tab.Tablature.Tests;

/// <summary>Verifies conventional ASCII tablature output and instrument-specific string counts.</summary>
public sealed class TablatureTextWriterTests
{
    /// <summary>Writes six labeled strings with fret numbers aligned by time and string.</summary>
    [Fact]
    public void Write_TrackWithNotesAndChord_ProducesClassicTabLayout()
    {
        var chord = new ChordEvent(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(250),
            [
                new FrettedNote(new StringNumber(3), new FretNumber(5)),
                new FrettedNote(new StringNumber(1), new FretNumber(3))
            ],
            "D major");
        var note = new NoteEvent(
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromMilliseconds(500),
            new StringNumber(2),
            new FretNumber(7),
            NoteTechnique.Bend | NoteTechnique.Vibrato);
        var openNote = new NoteEvent(
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(500),
            new StringNumber(6),
            new FretNumber(0));
        var track = CreateTrack([note, openNote], [chord]);
        var song = new Song("Test Song", "Artist", [track]);
        using var output = new StringWriter();

        new TablatureTextWriter().Write(song, track, output);

        Assert.Equal(string.Join('\n',
        [
            "Artist: Artist",
            "Song: Test Song",
            "Tuning: E2 A2 D3 G3 B3 E4",
            "Tuning reference: A4=440.00 Hz (0 cents)",
            string.Empty,
            "e|0------------|",
            "B|-------------|",
            "G|-------------|",
            "D|------------5|",
            "A|-----7b~-----|",
            "E|------------3|",
            string.Empty
        ]), output.ToString());
    }

    /// <summary>Formats slides and muted notes with common ASCII tab markers.</summary>
    [Fact]
    public void Write_SlideAndMutedNotes_UsesTabNotation()
    {
        var track = CreateTrack(
        [
            new NoteEvent(TimeSpan.Zero, TimeSpan.Zero, new StringNumber(1), new FretNumber(7), NoteTechnique.Slide),
            new NoteEvent(TimeSpan.FromMilliseconds(125), TimeSpan.Zero, new StringNumber(1), new FretNumber(5), NoteTechnique.Muted)
        ], []);
        var song = new Song("Song", "Artist", [track]);
        using var output = new StringWriter();

        new TablatureTextWriter().Write(song, track, output);

        Assert.Contains("E|su7-x|", output.ToString());
    }

    /// <summary>Starts a labeled tab segment at each repeated section marker.</summary>
    [Fact]
    public void Write_RepeatedSections_AddsNumberedHeadingsAtSectionBoundaries()
    {
        var track = CreateTrack(
        [
            new NoteEvent(TimeSpan.FromMilliseconds(250), TimeSpan.Zero, new StringNumber(1), new FretNumber(1)),
            new NoteEvent(TimeSpan.FromMilliseconds(750), TimeSpan.Zero, new StringNumber(1), new FretNumber(2)),
            new NoteEvent(TimeSpan.FromMilliseconds(1250), TimeSpan.Zero, new StringNumber(1), new FretNumber(3))
        ], []);
        var song = new Song("Song", "Artist", [track],
        [
            new SongSection("intro", 1, TimeSpan.Zero),
            new SongSection("verse", 1, TimeSpan.FromMilliseconds(500)),
            new SongSection("verse", 2, TimeSpan.FromSeconds(1))
        ]);
        using var output = new StringWriter();

        new TablatureTextWriter().Write(song, track, output);

        Assert.Contains("[Intro]\n", output.ToString());
        Assert.Contains("[Verse 1]\n", output.ToString());
        Assert.Contains("[Verse 2]\n", output.ToString());
        Assert.True(output.ToString().IndexOf("[Intro]", StringComparison.Ordinal) <
            output.ToString().IndexOf("[Verse 1]", StringComparison.Ordinal));
        Assert.True(output.ToString().IndexOf("[Verse 1]", StringComparison.Ordinal) <
            output.ToString().IndexOf("[Verse 2]", StringComparison.Ordinal));
        Assert.Contains("E|1|", output.ToString());
        Assert.Contains("E|2|", output.ToString());
        Assert.Contains("E|3|", output.ToString());
    }

    /// <summary>Omits empty sections while retaining notes and chords from populated sections.</summary>
    [Fact]
    public void Write_EmptySection_OmitsHeadingAndPreservesEvents()
    {
        var track = CreateTrack(
            [new NoteEvent(TimeSpan.FromMilliseconds(250), TimeSpan.Zero, new StringNumber(1), new FretNumber(1))],
            [new ChordEvent(TimeSpan.FromMilliseconds(1250), TimeSpan.Zero,
                [new FrettedNote(new StringNumber(1), new FretNumber(3))])]);
        var song = new Song("Song", "Artist", [track],
        [
            new SongSection("intro", 1, TimeSpan.Zero),
            new SongSection("NoGuitar1", 1, TimeSpan.FromMilliseconds(500)),
            new SongSection("verse", 1, TimeSpan.FromSeconds(1))
        ]);
        using var output = new StringWriter();

        new TablatureTextWriter().Write(song, track, output);

        Assert.Contains("[Intro]\n", output.ToString());
        Assert.DoesNotContain("[Noguitar1]", output.ToString());
        Assert.Contains("[Verse]\n", output.ToString());
        Assert.Contains("E|1", output.ToString());
        Assert.Contains("E|3", output.ToString());
    }

    /// <summary>Keeps a NoGuitar-named section when it contains note data.</summary>
    [Fact]
    public void Write_NoGuitarSectionWithNote_PreservesHeadingAndNote()
    {
        var track = CreateTrack(
            [new NoteEvent(TimeSpan.FromSeconds(1), TimeSpan.Zero, new StringNumber(1), new FretNumber(7))], []);
        var song = new Song("Song", "Artist", [track],
            [new SongSection("NoGuitar1", 1, TimeSpan.Zero)]);
        using var output = new StringWriter();

        new TablatureTextWriter().Write(song, track, output);

        Assert.Contains("[Noguitar1]\n", output.ToString());
        Assert.Contains("E|7|", output.ToString());
    }

    /// <summary>Produces identical output when the same song is written repeatedly.</summary>
    [Fact]
    public void Write_RepeatedCalls_AreDeterministic()
    {
        var track = CreateTrack([], []);
        var song = new Song("Song", "Artist", [track]);
        var tabWriter = new TablatureTextWriter();
        using var firstOutput = new StringWriter();
        using var secondOutput = new StringWriter();

        tabWriter.Write(song, track, firstOutput);
        tabWriter.Write(song, track, secondOutput);

        Assert.Equal(firstOutput.ToString(), secondOutput.ToString());
        Assert.StartsWith(
            "Artist: Artist\nSong: Song\nTuning: E2 A2 D3 G3 B3 E4\nTuning reference: A4=440.00 Hz (0 cents)\n\n",
            firstOutput.ToString());
    }

    /// <summary>Displays zero, positive, and negative cent offsets as A4 reference frequencies.</summary>
    /// <param name="centOffset">The arrangement offset.</param>
    /// <param name="expected">The expected tab header value.</param>
    [Theory]
    [InlineData(0, "Tuning reference: A4=440.00 Hz (0 cents)")]
    [InlineData(33, "Tuning reference: A4=448.47 Hz (+33 cents)")]
    [InlineData(-33, "Tuning reference: A4=431.69 Hz (-33 cents)")]
    public void Write_CentOffset_FormatsReferenceFrequency(decimal centOffset, string expected)
    {
        var track = CreateTrack([], []);
        var song = new Song("Song", "Artist", [track], centOffsetCents: centOffset);
        using var output = new StringWriter();

        new TablatureTextWriter().Write(song, track, output);

        Assert.Contains(expected, output.ToString());
    }

    /// <summary>Renders at least four strings for bass and extends the layout when a higher string is used.</summary>
    [Theory]
    [InlineData(4, "Tuning: E1 A1 D2 G2", new[] { "g|", "D|", "A|", "E|" })]
    [InlineData(5, "Tuning: E1 A1 D2 G2 B2", new[] { "b|", "G|", "D|", "A|", "E|" })]
    [InlineData(6, "Tuning: E1 A1 D2 G2 B2 E3", new[] { "e|", "B|", "G|", "D|", "A|", "E|" })]
    public void Write_Bass_UsesMinimumFourStringsAndExtendsForUsedStrings(int highestString,
        string expectedTuning, string[] expectedRows)
    {
        var bass = new Track("Bass", new GuitarTuning([28, 33, 38, 43, 47, 52]),
            [new NoteEvent(TimeSpan.Zero, TimeSpan.Zero, new StringNumber(highestString), new FretNumber(1))], []);
        var song = new Song("Song", "Artist", [bass]);
        using var output = new StringWriter();

        new TablatureTextWriter().Write(song, bass, output);

        Assert.Contains(expectedTuning, output.ToString());
        foreach (var row in expectedRows)
        {
            Assert.Contains(row, output.ToString());
        }
        Assert.Equal(expectedRows.Length, output.ToString().Split('\n').Count(line => line.Contains('|')));
    }

    /// <summary>Uses four rows for an empty bass and includes higher strings used only by chord tones.</summary>
    [Fact]
    public void Write_Bass_EmptyAndChordEvents_UseExpectedStringCount()
    {
        var emptyBass = new Track("Bass", new GuitarTuning([28, 33, 38, 43, 47, 52]), [], []);
        var emptySong = new Song("Song", "Artist", [emptyBass]);
        using var emptyOutput = new StringWriter();
        new TablatureTextWriter().Write(emptySong, emptyBass, emptyOutput);

        Assert.Equal(4, emptyOutput.ToString().Split('\n').Count(line => line.Contains('|')));

        var chordBass = new Track("Bass", new GuitarTuning([28, 33, 38, 43, 47, 52]), [],
        [
            new ChordEvent(TimeSpan.Zero, TimeSpan.Zero,
                [new FrettedNote(new StringNumber(5), new FretNumber(3))])
        ]);
        var chordSong = new Song("Song", "Artist", [chordBass]);
        using var chordOutput = new StringWriter();
        new TablatureTextWriter().Write(chordSong, chordBass, chordOutput);

        Assert.Equal(5, chordOutput.ToString().Split('\n').Count(line => line.Contains('|')));
        Assert.Contains("b|3|", chordOutput.ToString());
    }

    /// <summary>Keeps all six guitar rows even when the arrangement only uses its lowest string.</summary>
    [Fact]
    public void Write_Guitar_AlwaysRendersSixStrings()
    {
        var guitar = CreateTrack(
            [new NoteEvent(TimeSpan.Zero, TimeSpan.Zero, new StringNumber(1), new FretNumber(1))], []);
        var song = new Song("Song", "Artist", [guitar]);
        using var output = new StringWriter();

        new TablatureTextWriter().Write(song, guitar, output);

        Assert.Contains("Tuning: E2 A2 D3 G3 B3 E4", output.ToString());
        Assert.Equal(6, output.ToString().Split('\n').Count(line => line.Contains('|')));
    }

    /// <summary>Rejects a track that does not belong to the supplied song.</summary>
    [Fact]
    public void Write_TrackNotInSong_ThrowsArgumentException()
    {
        var songTrack = CreateTrack([], []);
        var otherTrack = CreateTrack([], []);
        var song = new Song("Song", "Artist", [songTrack]);
        using var output = new StringWriter();

        Assert.Throws<ArgumentException>(() => new TablatureTextWriter().Write(song, otherTrack, output));
    }

    /// <summary>Creates a six-string lead track with caller-supplied events.</summary>
    /// <param name="notes">The individual note events.</param>
    /// <param name="chords">The chord events.</param>
    /// <returns>A valid lead track in standard E tuning.</returns>
    private static Track CreateTrack(IEnumerable<NoteEvent> notes, IEnumerable<ChordEvent> chords) =>
        new("Lead", new GuitarTuning([40, 45, 50, 55, 59, 64]), notes, chords);
}
