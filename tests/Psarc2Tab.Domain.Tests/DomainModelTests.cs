using Psarc2Tab.Domain;

namespace Psarc2Tab.Domain.Tests;

/// <summary>Verifies invariants and immutable snapshots in the musical domain model.</summary>
public sealed class DomainModelTests
{
    /// <summary>Preserves string pitches in the supplied tuning order.</summary>
    [Fact]
    public void GuitarTuning_StoresPitchesInOrder()
    {
        var tuning = new GuitarTuning([40, 45, 50, 55, 59, 64]);

        Assert.Equal(new[] { 40, 45, 50, 55, 59, 64 }, tuning.OpenStringMidiPitches);
        Assert.Equal(6, tuning.StringCount);
    }

    /// <summary>Rejects an empty tuning.</summary>
    [Fact]
    public void GuitarTuning_EmptyPitches_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new GuitarTuning([]));
    }

    /// <summary>Rejects pitches outside the MIDI range.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(128)]
    public void GuitarTuning_InvalidMidiPitch_ThrowsArgumentOutOfRangeException(int pitch)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GuitarTuning([pitch]));
    }

    /// <summary>Validates one-based string and nonnegative fret value objects.</summary>
    [Fact]
    public void PositionValueObjects_RejectInvalidValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new StringNumber(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FretNumber(-1));
    }

    /// <summary>Stores note timing, position, and combined technique flags.</summary>
    [Fact]
    public void NoteEvent_StoresItsMusicalValues()
    {
        var note = new NoteEvent(
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromMilliseconds(500),
            new StringNumber(2),
            new FretNumber(7),
            NoteTechnique.Bend | NoteTechnique.Vibrato);

        Assert.Equal(TimeSpan.FromMilliseconds(250), note.Position);
        Assert.Equal(TimeSpan.FromMilliseconds(500), note.Duration);
        Assert.Equal(new StringNumber(2), note.StringNumber);
        Assert.Equal(new FretNumber(7), note.FretNumber);
        Assert.Equal(NoteTechnique.Bend | NoteTechnique.Vibrato, note.Techniques);
    }

    /// <summary>Rejects negative event timing and unsupported technique flags.</summary>
    [Fact]
    public void NoteEvent_InvalidTimeOrTechnique_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new NoteEvent(
            TimeSpan.FromTicks(-1), TimeSpan.Zero, new StringNumber(1), new FretNumber(0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NoteEvent(
            TimeSpan.Zero, TimeSpan.Zero, new StringNumber(1), new FretNumber(0), (NoteTechnique)(1 << 20)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NoteEvent(
            TimeSpan.Zero, TimeSpan.Zero, default, new FretNumber(0)));
    }

    /// <summary>Stores a chord as distinct fretted notes with common timing.</summary>
    [Fact]
    public void ChordEvent_StoresNotesAndLabel()
    {
        var notes = new[]
        {
            new FrettedNote(new StringNumber(1), new FretNumber(3)),
            new FrettedNote(new StringNumber(2), new FretNumber(5))
        };
        var chord = new ChordEvent(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), notes, "Example");

        Assert.Equal(notes, chord.Notes);
        Assert.Equal("Example", chord.Label);
    }

    /// <summary>Rejects empty chords and duplicate strings.</summary>
    [Fact]
    public void ChordEvent_EmptyOrDuplicateStrings_ThrowsArgumentException()
    {
        var position = TimeSpan.Zero;
        var duration = TimeSpan.FromSeconds(1);
        var note = new FrettedNote(new StringNumber(1), new FretNumber(0));

        Assert.Throws<ArgumentException>(() => new ChordEvent(position, duration, []));
        Assert.Throws<ArgumentException>(() => new ChordEvent(position, duration, [note, note]));
        Assert.Throws<ArgumentException>(() => new ChordEvent(position, duration, [default]));
    }

    /// <summary>Rejects track events that refer to strings absent from its tuning.</summary>
    [Fact]
    public void Track_EventUsesUnknownString_ThrowsArgumentException()
    {
        var tuning = new GuitarTuning([40, 45]);
        var note = new NoteEvent(TimeSpan.Zero, TimeSpan.FromSeconds(1), new StringNumber(3), new FretNumber(0));

        Assert.Throws<ArgumentException>(() => new Track("Bass", tuning, [note], []));
    }

    /// <summary>Creates a song with a track and retains a read-only snapshot.</summary>
    [Fact]
    public void Song_CreatesReadOnlyTrackCollection()
    {
        var track = CreateTrack();
        var tracks = new List<Track> { track };
        var song = new Song("Example", "Artist", tracks);
        tracks.Clear();

        Assert.Equal("Example", song.Title);
        Assert.Equal("Artist", song.Artist);
        Assert.Single(song.Tracks);
        Assert.Same(track, song.Tracks[0]);
        Assert.Throws<NotSupportedException>(() => ((IList<Track>)song.Tracks).Add(CreateTrack()));
    }

    /// <summary>Rejects blank song titles and songs without tracks.</summary>
    [Fact]
    public void Song_BlankTitleOrNoTracks_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new Song(" ", "Artist", [CreateTrack()]));
        Assert.Throws<ArgumentException>(() => new Song("Example", "Artist", []));
    }

    /// <summary>Builds a small valid track for domain tests.</summary>
    /// <returns>A six-string track with no note events.</returns>
    private static Track CreateTrack() =>
        new("Lead", new GuitarTuning([40, 45, 50, 55, 59, 64]), [], []);
}
