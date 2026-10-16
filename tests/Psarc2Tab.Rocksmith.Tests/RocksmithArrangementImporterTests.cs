using System.Text;
using Psarc2Tab.Domain;
using Psarc2Tab.Formats.Rocksmith;

namespace Psarc2Tab.Rocksmith.Tests;

/// <summary>Tests conversion from Rocksmith arrangement XML into the domain model.</summary>
public sealed class RocksmithArrangementImporterTests
{
    /// <summary>Imports metadata, notes, techniques, chords, and the highest difficulty.</summary>
    [Fact]
    public void Import_ValidArrangement_MapsDataToDomainModel()
    {
        const string xml = """
            <song version="7">
              <title>Example Song</title><arrangement>Lead</arrangement><artistName>Example Artist</artistName>
              <tuning string0="0" string1="0" string2="0" string3="0" string4="0" string5="0" />
              <chordTemplates><chordTemplate displayName="D" fret0="-1" fret1="-1" fret2="0" fret3="2" fret4="3" fret5="2" /></chordTemplates>
              <sections>
                <section name="intro" number="1" startTime="0" />
                <section name="verse" number="1" startTime="14.89" />
                <section name="verse" number="2" startTime="24.219" />
              </sections>
              <levels>
                <level difficulty="0"><notes><note time="1.25" sustain="0.5" string="0" fret="3" /></notes><chords /></level>
                <level difficulty="1">
                  <notes>
                    <note time="2.125" sustain="0.25" string="2" fret="5" hammerOn="1" bend="2" slideTo="7" vibrato="80" harmonicPinch="1" palmMute="1" accent="1" tap="1" slap="1" pluck="1" tremolo="1" mute="1" />
                    <note time="3" sustain="0" string="1" fret="0" ignore="1" />
                  </notes>
                  <chords><chord time="4.5" chordId="0" /></chords>
                </level>
              </levels>
            </song>
            """;

        var song = Import(xml);

        Assert.Equal("Example Song", song.Title);
        Assert.Equal("Example Artist", song.Artist);
        var track = Assert.Single(song.Tracks);
        Assert.Equal("Lead", track.Name);
        Assert.Equal(new[] { 40, 45, 50, 55, 59, 64 }, track.Tuning.OpenStringMidiPitches);
        var note = Assert.Single(track.Notes);
        Assert.Equal(TimeSpan.FromSeconds(2.125), note.Position);
        Assert.Equal(TimeSpan.FromSeconds(0.25), note.Duration);
        Assert.Equal(3, note.StringNumber.Value);
        Assert.Equal(5, note.FretNumber.Value);
        Assert.Equal(NoteTechnique.HammerOn | NoteTechnique.Bend | NoteTechnique.Slide | NoteTechnique.Vibrato |
            NoteTechnique.Harmonic | NoteTechnique.PalmMute | NoteTechnique.Accent | NoteTechnique.Tap |
            NoteTechnique.Slap | NoteTechnique.Pluck | NoteTechnique.Tremolo | NoteTechnique.Muted, note.Techniques);
        var chord = Assert.Single(track.Chords);
        Assert.Equal(TimeSpan.FromSeconds(4.5), chord.Position);
        Assert.Equal(TimeSpan.Zero, chord.Duration);
        Assert.Equal("D", chord.Label);
        Assert.Equal(new[] { 3, 4, 5, 6 }, chord.Notes.Select(item => item.StringNumber.Value));
        Assert.Collection(song.Sections,
            section => Assert.Equal(new SongSection("intro", 1, TimeSpan.Zero), section),
            section => Assert.Equal(new SongSection("verse", 1, TimeSpan.FromSeconds(14.89)), section),
            section => Assert.Equal(new SongSection("verse", 2, TimeSpan.FromSeconds(24.219)), section));
    }

    /// <summary>Imports bass tuning using bass-register MIDI pitches and the XML string numbering.</summary>
    [Fact]
    public void Import_BassArrangement_UsesBassPitchRange()
    {
        var song = Import(CreateXml("Bass", "<note time=\"0\" sustain=\"0\" string=\"0\" fret=\"0\" />"));

        Assert.Equal(new[] { 28, 33, 38, 43, 47, 52 }, Assert.Single(song.Tracks).Tuning.OpenStringMidiPitches);
        Assert.Equal(1, Assert.Single(Assert.Single(song.Tracks).Notes).StringNumber.Value);
    }

    /// <summary>Allows arrangements without a sections element.</summary>
    [Fact]
    public void Import_MissingSections_ReturnsNoSectionMarkers()
    {
        var song = Import(CreateXml("Lead", string.Empty));

        Assert.Empty(song.Sections);
    }

    /// <summary>Imports positive and negative cent offsets from arrangement XML.</summary>
    /// <param name="centOffset">The XML cent offset.</param>
    [Theory]
    [InlineData("33.5", 33.5)]
    [InlineData("-33.5", -33.5)]
    public void Import_CentOffset_MapsToSong(string centOffset, decimal expected)
    {
        var xml = CreateXml("Lead", "").Replace("<artistName />",
            $"<artistName /><centOffset>{centOffset}</centOffset>", StringComparison.Ordinal);

        var song = Import(xml);

        Assert.Equal(expected, song.CentOffsetCents);
    }

    /// <summary>Rejects unsupported roots and arrangement labels.</summary>
    /// <param name="xml">The unsupported document.</param>
    [Theory]
    [InlineData("<vocals />")]
    [InlineData("<song><title>T</title><arrangement>Vocals</arrangement></song>")]
    public void Import_UnsupportedDocument_ThrowsInvalidDataException(string xml)
    {
        Assert.Throws<InvalidDataException>(() => Import(xml));
    }

    /// <summary>Reports missing required chart structures as invalid input.</summary>
    /// <param name="xml">A chart missing one required field or collection.</param>
    [Theory]
    [InlineData("<song><title /><arrangement>Lead</arrangement><tuning string0=\"0\" string1=\"0\" string2=\"0\" string3=\"0\" string4=\"0\" string5=\"0\" /><levels><level difficulty=\"0\"><notes /><chords /></level></levels></song>")]
    [InlineData("<song><title>Song</title><arrangement>Lead</arrangement><levels><level difficulty=\"0\"><notes /><chords /></level></levels></song>")]
    [InlineData("<song><title>Song</title><arrangement>Lead</arrangement><tuning string0=\"0\" string1=\"0\" string2=\"0\" string3=\"0\" string4=\"0\" string5=\"0\" /><levels /></song>")]
    public void Import_MissingRequiredData_ThrowsInvalidDataException(string xml)
    {
        Assert.Throws<InvalidDataException>(() => Import(xml));
    }

    /// <summary>Rejects a document containing a DTD.</summary>
    [Fact]
    public void Import_DtdDocument_ThrowsInvalidDataException()
    {
        const string xml = "<!DOCTYPE song [<!ENTITY title 'Unsafe'>]><song><title>&title;</title></song>";

        Assert.Throws<InvalidDataException>(() => Import(xml));
    }

    /// <summary>Rejects nonzero song offsets whose timeline meaning is not yet verified.</summary>
    [Fact]
    public void Import_NonzeroSongOffset_ThrowsInvalidDataException()
    {
        var xml = CreateXml("Lead", "").Replace("<artistName />", "<artistName /><offset>0.125</offset>", StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => Import(xml));
    }

    /// <summary>Creates a minimal valid arrangement XML document.</summary>
    /// <param name="arrangement">The arrangement label.</param>
    /// <param name="note">The note XML inside the level.</param>
    /// <returns>A valid arrangement document.</returns>
    private static string CreateXml(string arrangement, string note) => $"""
        <song version="7">
          <title>Test Song</title><arrangement>{arrangement}</arrangement><artistName />
          <tuning string0="0" string1="0" string2="0" string3="0" string4="0" string5="0" />
          <chordTemplates />
          <levels><level difficulty="0"><notes>{note}</notes><chords /></level></levels>
        </song>
        """;

    /// <summary>Imports UTF-8 XML from an in-memory stream.</summary>
    /// <param name="xml">The XML document.</param>
    /// <returns>The imported song.</returns>
    private static Song Import(string xml)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        return new RocksmithArrangementImporter().Import(stream);
    }
}
