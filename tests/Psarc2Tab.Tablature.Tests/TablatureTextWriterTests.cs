using Psarc2Tab.Domain;
using Psarc2Tab.Tablature;

namespace Psarc2Tab.Tablature.Tests;

/// <summary>Verifies deterministic output in the version 1 text tab format.</summary>
public sealed class TablatureTextWriterTests
{
    /// <summary>Writes metadata, tuning, timed notes, and chord tones in stable order.</summary>
    [Fact]
    public void Write_TrackWithNotesAndChord_ProducesDocumentedText()
    {
        var chord = new ChordEvent(
            TimeSpan.FromTicks(1_252_500),
            TimeSpan.FromTicks(2_505_000),
            [
                new FrettedNote(new StringNumber(3), new FretNumber(5)),
                new FrettedNote(new StringNumber(1), new FretNumber(3))
            ],
            "D major");
        var note = new NoteEvent(
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromMilliseconds(500),
            new StringNumber(2),
            new FretNumber(7),
            NoteTechnique.Bend | NoteTechnique.Vibrato);
        var openNote = new NoteEvent(
            TimeSpan.Zero,
            TimeSpan.FromTicks(1_005_000),
            new StringNumber(6),
            new FretNumber(0));
        var track = CreateTrack([note, openNote], [chord]);
        var song = new Song("Test Song", "Artist", [track]);
        using var output = new StringWriter();

        new TablatureTextWriter().Write(song, track, output);

        Assert.Equal(string.Join('\n',
        [
            "# psarc2tab-tab/1",
            "song\t\"Test Song\"",
            "artist\t\"Artist\"",
            "track\t\"Lead\"",
            "tuning_midi\t40,45,50,55,59,64",
            "position_ms\tstring\tfret\tduration_ms\tevent\ttechniques\tchord_label",
            "0\t6\t0\t100.5\tnote\tNone\t\"\"",
            "125.25\t1\t3\t250.5\tchord\tNone\t\"D major\"",
            "125.25\t3\t5\t250.5\tchord\tNone\t\"D major\"",
            "250\t2\t7\t500\tnote\tBend|Vibrato\t\"\"",
            string.Empty
        ]), output.ToString());
    }

    /// <summary>Escapes metadata values and produces identical output on repeated writes.</summary>
    [Fact]
    public void Write_EscapedMetadata_IsDeterministic()
    {
        var track = CreateTrack([], []);
        var song = new Song("Song\tTitle", "Artist\nName", [track]);
        var tabWriter = new TablatureTextWriter();
        using var firstOutput = new StringWriter();
        using var secondOutput = new StringWriter();

        tabWriter.Write(song, track, firstOutput);
        tabWriter.Write(song, track, secondOutput);

        Assert.Equal(firstOutput.ToString(), secondOutput.ToString());
        Assert.Contains("song\t\"Song\\tTitle\"\n", firstOutput.ToString());
        Assert.Contains("artist\t\"Artist\\nName\"\n", firstOutput.ToString());
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
