using Psarc2Tab.Domain;
using Psarc2Tab.Tablature;

namespace Psarc2Tab.Tablature.Tests;

/// <summary>Verifies conventional six-line ASCII tablature output.</summary>
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
        Assert.StartsWith("e||\nB||\nG||", firstOutput.ToString());
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
