using System.Buffers.Binary;
using System.Text;
using Psarc2Tab.Domain;

namespace Psarc2Tab.Formats.Rocksmith;

/// <summary>Converts an uncompressed Rocksmith SNG chart to a song domain model.</summary>
public sealed class SngChartImporter
{
    /// <summary>The mask identifying chord events.</summary>
    private const uint ChordMask = 0x0000_0002;
    /// <summary>The mask identifying ignored notes.</summary>
    private const uint IgnoreMask = 0x0004_0000;
    /// <summary>The mask identifying open notes.</summary>
    private const uint OpenMask = 0x0000_0004;
    /// <summary>The mask identifying tremolo notes.</summary>
    private const uint TremoloMask = 0x0000_0010;
    /// <summary>The mask identifying harmonic notes.</summary>
    private const uint HarmonicMask = 0x0000_0020;
    /// <summary>The mask identifying palm-muted notes.</summary>
    private const uint PalmMuteMask = 0x0000_0040;
    /// <summary>The mask identifying slap notes.</summary>
    private const uint SlapMask = 0x0000_0080;
    /// <summary>The mask identifying plucked notes.</summary>
    private const uint PluckMask = 0x0000_0100;
    /// <summary>The mask identifying hammer-ons.</summary>
    private const uint HammerOnMask = 0x0000_0200;
    /// <summary>The mask identifying pull-offs.</summary>
    private const uint PullOffMask = 0x0000_0400;
    /// <summary>The mask identifying slides.</summary>
    private const uint SlideMask = 0x0000_0800;
    /// <summary>The mask identifying bends.</summary>
    private const uint BendMask = 0x0000_1000;
    /// <summary>The mask identifying tapped notes.</summary>
    private const uint TapMask = 0x0000_4000;
    /// <summary>The mask identifying vibrato.</summary>
    private const uint VibratoMask = 0x0001_0000;
    /// <summary>The mask identifying muted notes.</summary>
    private const uint MutedMask = 0x0002_0000;
    /// <summary>The mask identifying accents.</summary>
    private const uint AccentMask = 0x0400_0000;
    /// <summary>The maximum count accepted for a chart array.</summary>
    private const int MaximumArrayCount = 1_000_000;
    /// <summary>The six standard guitar pitches used to apply arrangement tuning offsets.</summary>
    private static readonly int[] GuitarPitches = [40, 45, 50, 55, 59, 64];

    /// <summary>Reads the selected chart at its highest declared difficulty.</summary>
    /// <param name="chart">The decoded chart bytes.</param>
    /// <param name="arrangement">The arrangement metadata and chart byte order.</param>
    /// <returns>A song containing one imported track.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="InvalidDataException">The chart is malformed or uses unsupported values.</exception>
    public Song Import(ReadOnlySpan<byte> chart, RocksmithArrangement arrangement)
    {
        ArgumentNullException.ThrowIfNull(arrangement);
        var metadata = arrangement.SngMetadata
            ?? throw new InvalidDataException("The SNG arrangement is missing required song metadata.");
        if (metadata.TuningOffsets.Count != 6)
        {
            throw new InvalidDataException("The SNG arrangement must declare six tuning positions.");
        }

        var reader = new ChartReader(chart, SngContainerReader.GetPlatform(arrangement.ArchivePath) == SngPlatform.Mac);
        var beats = reader.ReadCount();
        reader.SkipRecords(beats, 16);
        var phraseCount = reader.ReadCount();
        reader.SkipRecords(phraseCount, 44);
        var chordCount = reader.ReadCount();
        var chordTemplates = new ChordTemplate[chordCount];
        for (var index = 0; index < chordCount; index++)
        {
            chordTemplates[index] = reader.ReadChordTemplate();
        }

        reader.SkipRecords(reader.ReadCount(), 2376);
        reader.SkipRecords(reader.ReadCount(), 60);
        var phraseIterations = new PhraseIteration[reader.ReadCount()];
        for (var index = 0; index < phraseIterations.Length; index++)
        {
            phraseIterations[index] = reader.ReadPhraseIteration(phraseCount);
        }
        reader.SkipRecords(reader.ReadCount(), 16);
        reader.SkipNld();
        reader.SkipRecords(reader.ReadCount(), 260);
        reader.SkipRecords(reader.ReadCount(), 260);
        reader.SkipRecords(reader.ReadCount(), 8);
        reader.SkipRecords(reader.ReadCount(), 8);
        var sections = new SongSection[reader.ReadCount()];
        for (var index = 0; index < sections.Length; index++)
        {
            sections[index] = reader.ReadSection();
        }

        var levelCount = reader.ReadCount();
        if (levelCount == 0)
        {
            throw new InvalidDataException($"The SNG chart has no difficulty levels at byte {reader.Offset} (count {levelCount}).");
        }

        List<SngNote> selectedNotes = [];
        for (var levelIndex = 0; levelIndex < levelCount; levelIndex++)
        {
            var difficulty = reader.ReadInt32();
            reader.SkipRecords(reader.ReadCount(), 28);
            reader.SkipRecords(reader.ReadCount(), 12);
            reader.SkipRecords(reader.ReadCount(), 20);
            reader.SkipRecords(reader.ReadCount(), 20);
            var noteCount = reader.ReadCount();
            for (var noteIndex = 0; noteIndex < noteCount; noteIndex++)
            {
                var note = reader.ReadNote();
                if ((uint)note.PhraseIterationId >= (uint)phraseIterations.Length)
                {
                    throw new InvalidDataException("An SNG note references a missing phrase iteration.");
                }

                if (difficulty == phraseIterations[note.PhraseIterationId].MasterDifficulty)
                {
                    selectedNotes.Add(note);
                }
            }

            reader.SkipRecords(reader.ReadCount(), 4);
            reader.SkipRecords(reader.ReadCount(), 4);
            reader.SkipRecords(reader.ReadCount(), 4);
        }

        var tuningBase = string.Equals(arrangement.Instrument, "Bass", StringComparison.Ordinal) ? -12 : 0;
        var tuning = new GuitarTuning(GuitarPitches.Select((pitch, index) =>
            checked(pitch + tuningBase + metadata.TuningOffsets[index])));
        var notesResult = new List<NoteEvent>();
        var chordsResult = new List<ChordEvent>();
        foreach (var note in selectedNotes)
        {
            if ((note.Mask & IgnoreMask) != 0)
            {
                continue;
            }

            if (!float.IsFinite(note.Time) || note.Time < 0 || !float.IsFinite(note.Sustain) || note.Sustain < 0)
            {
                throw new InvalidDataException("The SNG chart contains invalid note timing.");
            }

            if ((note.Mask & ChordMask) != 0)
            {
                if ((uint)note.ChordId >= (uint)chordTemplates.Length)
                {
                    throw new InvalidDataException("An SNG chord references a missing chord template.");
                }

                var template = chordTemplates[note.ChordId];
                var fretted = template.Frets.Select((fret, index) => (fret, index))
                    .Where(item => item.fret >= 0)
                    .Select(item => new FrettedNote(new StringNumber(item.index + 1), new FretNumber(item.fret)))
                    .ToArray();
                if (fretted.Length > 0)
                {
                    chordsResult.Add(new ChordEvent(TimeSpan.FromSeconds(note.Time), TimeSpan.FromSeconds(note.Sustain),
                        fretted, template.Name));
                }

                continue;
            }

            if (note.StringIndex is < 0 or >= 6 || note.Fret < 0)
            {
                throw new InvalidDataException("An SNG note has a string or fret value outside the supported range.");
            }

            var fret = (note.Mask & OpenMask) != 0 ? 0 : note.Fret;
            notesResult.Add(new NoteEvent(TimeSpan.FromSeconds(note.Time), TimeSpan.FromSeconds(note.Sustain),
                new StringNumber(note.StringIndex + 1), new FretNumber(fret), MapTechniques(note.Mask)));
        }

        return new Song(metadata.Title, metadata.Artist,
            [new Track(arrangement.Instrument, tuning, notesResult, chordsResult)], sections);
    }

    /// <summary>Reads timed lyric entries from a decoded SNG chart.</summary>
    /// <param name="chart">The decoded chart bytes.</param>
    /// <param name="archivePath">The archive path used to determine chart byte order.</param>
    /// <returns>The lyrics in chart order.</returns>
    /// <exception cref="ArgumentException"><paramref name="archivePath"/> is not a supported SNG path.</exception>
    /// <exception cref="InvalidDataException">The chart is malformed or contains invalid lyric data.</exception>
    public SongLyrics ImportLyrics(ReadOnlySpan<byte> chart, string archivePath)
    {
        var reader = new ChartReader(chart, SngContainerReader.GetPlatform(archivePath) == SngPlatform.Mac);
        reader.SkipRecords(reader.ReadCount(), 16);
        reader.SkipRecords(reader.ReadCount(), 44);
        reader.SkipRecords(reader.ReadCount(), 72);
        reader.SkipRecords(reader.ReadCount(), 2376);
        var lyrics = new TimedLyric[reader.ReadCount()];
        for (var index = 0; index < lyrics.Length; index++)
        {
            lyrics[index] = reader.ReadLyric();
        }

        return new SongLyrics(lyrics);
    }

    /// <summary>Maps the SNG note-mask technique bits to domain flags.</summary>
    /// <param name="mask">The note mask.</param>
    /// <returns>The techniques represented by the domain model.</returns>
    private static NoteTechnique MapTechniques(uint mask)
    {
        var techniques = NoteTechnique.None;
        if ((mask & HammerOnMask) != 0) techniques |= NoteTechnique.HammerOn;
        if ((mask & PullOffMask) != 0) techniques |= NoteTechnique.PullOff;
        if ((mask & BendMask) != 0) techniques |= NoteTechnique.Bend;
        if ((mask & SlideMask) != 0) techniques |= NoteTechnique.Slide;
        if ((mask & VibratoMask) != 0) techniques |= NoteTechnique.Vibrato;
        if ((mask & HarmonicMask) != 0) techniques |= NoteTechnique.Harmonic;
        if ((mask & PalmMuteMask) != 0) techniques |= NoteTechnique.PalmMute;
        if ((mask & AccentMask) != 0) techniques |= NoteTechnique.Accent;
        if ((mask & TapMask) != 0) techniques |= NoteTechnique.Tap;
        if ((mask & SlapMask) != 0) techniques |= NoteTechnique.Slap;
        if ((mask & PluckMask) != 0) techniques |= NoteTechnique.Pluck;
        if ((mask & TremoloMask) != 0) techniques |= NoteTechnique.Tremolo;
        if ((mask & MutedMask) != 0) techniques |= NoteTechnique.Muted;
        return techniques;
    }

    /// <summary>Stores a chord template used by chord notes.</summary>
    /// <param name="Frets">The fret value for each of the six string positions.</param>
    /// <param name="Name">The optional template label.</param>
    private sealed record ChordTemplate(sbyte[] Frets, string? Name);

    /// <summary>Stores the fields needed from an SNG note record.</summary>
    /// <param name="Mask">The note behavior bit field.</param>
    /// <param name="Time">The event time in seconds.</param>
    /// <param name="StringIndex">The zero-based string index.</param>
    /// <param name="Fret">The fret position.</param>
    /// <param name="ChordId">The referenced chord template index.</param>
    /// <param name="PhraseIterationId">The zero-based phrase iteration containing the note.</param>
    /// <param name="Sustain">The note sustain in seconds.</param>
    private sealed record SngNote(uint Mask, float Time, sbyte StringIndex, sbyte Fret, int ChordId,
        int PhraseIterationId, float Sustain);

    /// <summary>Stores per-phrase-iteration difficulty metadata.</summary>
    /// <param name="PhraseId">The phrase definition identifier.</param>
    /// <param name="StartTime">The phrase iteration start time in seconds.</param>
    /// <param name="NextPhraseTime">The next phrase iteration time in seconds.</param>
    /// <param name="MasterDifficulty">The full arrangement difficulty for this iteration.</param>
    private sealed record PhraseIteration(int PhraseId, float StartTime, float NextPhraseTime, int MasterDifficulty);

    /// <summary>Reads bounded primitive values and records from a chart buffer.</summary>
    private sealed class ChartReader
    {
        /// <summary>The decoded chart bytes.</summary>
        private readonly byte[] _data;
        /// <summary>Whether numeric fields use Mac byte order.</summary>
        private readonly bool _bigEndian;
        /// <summary>The current position in the decoded chart.</summary>
        private int _offset;

        /// <summary>Creates a reader over the decoded chart.</summary>
        /// <param name="data">The chart bytes.</param>
        /// <param name="bigEndian">Whether the chart uses Mac byte order.</param>
        public ChartReader(ReadOnlySpan<byte> data, bool bigEndian)
        {
            _data = data.ToArray();
            _bigEndian = bigEndian;
        }

        /// <summary>Gets the current chart position.</summary>
        public int Offset => _offset;

        /// <summary>Reads a validated non-negative array count.</summary>
        /// <returns>The array element count.</returns>
        public int ReadCount()
        {
            var count = ReadInt32();
            if (count < 0 || count > MaximumArrayCount)
            {
                throw new InvalidDataException("The SNG chart contains an invalid array count.");
            }

            return count;
        }

        /// <summary>Skips a fixed-size record array after checking the buffer bounds.</summary>
        /// <param name="count">The number of records.</param>
        /// <param name="recordSize">The bytes in each record.</param>
        public void SkipRecords(int count, int recordSize)
        {
            var byteCount = (long)count * recordSize;
            if (byteCount > int.MaxValue)
            {
                throw new InvalidDataException("The SNG chart record array exceeds the supported size.");
            }

            Skip((int)byteCount);
        }

        /// <summary>Reads one fixed-size chord template.</summary>
        /// <returns>The frets and label stored by the template.</returns>
        public ChordTemplate ReadChordTemplate()
        {
            Skip(4);
            var frets = new sbyte[6];
            for (var index = 0; index < frets.Length; index++) frets[index] = ReadSByte();
            Skip(6 + 24);
            var nameBytes = ReadBytes(32);
            var terminator = Array.IndexOf(nameBytes, (byte)0);
            var name = Encoding.UTF8.GetString(nameBytes, 0, terminator < 0 ? nameBytes.Length : terminator).Trim();
            return new ChordTemplate(frets, name.Length == 0 ? null : name);
        }

        /// <summary>Reads timing and per-performance difficulty values from one phrase iteration.</summary>
        /// <param name="phraseCount">The number of phrase definitions in the chart.</param>
        /// <returns>The phrase iteration's full arrangement difficulty.</returns>
        /// <exception cref="InvalidDataException">The phrase iteration contains invalid metadata.</exception>
        public PhraseIteration ReadPhraseIteration(int phraseCount)
        {
            var phraseId = ReadInt32();
            var startTime = ReadSingle();
            var nextPhraseTime = ReadSingle();
            _ = ReadInt32();
            _ = ReadInt32();
            var masterDifficulty = ReadInt32();

            if ((uint)phraseId >= (uint)phraseCount || !float.IsFinite(startTime) || startTime < 0 ||
                !float.IsFinite(nextPhraseTime) || nextPhraseTime < startTime || masterDifficulty < 0)
            {
                throw new InvalidDataException("The SNG chart contains an invalid phrase iteration.");
            }

            return new PhraseIteration(phraseId, startTime, nextPhraseTime, masterDifficulty);
        }

        /// <summary>Reads the name, number, and start time from one SNG section record.</summary>
        /// <returns>The section marker used by tablature headings.</returns>
        public SongSection ReadSection()
        {
            var nameBytes = ReadBytes(32);
            var terminator = Array.IndexOf(nameBytes, (byte)0);
            var name = Encoding.UTF8.GetString(nameBytes, 0, terminator < 0 ? nameBytes.Length : terminator).Trim();
            var number = ReadInt32();
            var startTime = ReadSingle();
            var endTime = ReadSingle();
            Skip(8);
            Skip(36);

            if (string.IsNullOrWhiteSpace(name) || !float.IsFinite(startTime) || startTime < 0 ||
                !float.IsFinite(endTime) || endTime < startTime)
            {
                throw new InvalidDataException("The SNG chart contains an invalid section record.");
            }

            return new SongSection(name, number, TimeSpan.FromSeconds(startTime));
        }

        /// <summary>Skips the optional note-level-difficulty section.</summary>
        public void SkipNld()
        {
            var count = ReadCount();
            for (var index = 0; index < count; index++)
            {
                Skip(4);
                SkipRecords(ReadCount(), 4);
            }
        }

        /// <summary>Reads the useful fields from one variable-length note record.</summary>
        /// <returns>The decoded note fields.</returns>
        public SngNote ReadNote()
        {
            var mask = ReadUInt32();
            Skip(8);
            var time = ReadSingle();
            var stringIndex = ReadSByte();
            var fret = ReadSByte();
            Skip(2);
            var chordId = ReadInt32();
            Skip(sizeof(int));
            Skip(sizeof(int));
            var phraseIterationId = ReadInt32();
            Skip(5 * sizeof(short) + 7 + sizeof(short));
            var sustain = ReadSingle();
            Skip(sizeof(float));
            var bendCount = ReadCount();
            SkipRecords(bendCount, 12);
            return new SngNote(mask, time, stringIndex, fret, chordId, phraseIterationId, sustain);
        }

        /// <summary>Reads one timed lyric record with a fixed-width UTF-8 text field.</summary>
        /// <returns>The decoded lyric fragment.</returns>
        /// <exception cref="InvalidDataException">The lyric timing or text encoding is invalid.</exception>
        public TimedLyric ReadLyric()
        {
            var time = ReadSingle();
            _ = ReadInt32();
            var length = ReadSingle();
            var textBytes = ReadBytes(48);
            var terminator = Array.IndexOf(textBytes, (byte)0);
            string text;
            try
            {
                text = new UTF8Encoding(false, true).GetString(textBytes, 0,
                    terminator < 0 ? textBytes.Length : terminator).Trim();
            }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidDataException("The SNG chart contains lyric text that is not valid UTF-8.", exception);
            }

            if (!float.IsFinite(time) || time < 0 || !float.IsFinite(length) || length < 0)
            {
                throw new InvalidDataException("The SNG chart contains invalid lyric timing.");
            }

            if (text.Length == 0)
            {
                throw new InvalidDataException("The SNG chart contains an empty lyric record.");
            }

            return new TimedLyric(TimeSpan.FromSeconds(time), text);
        }

        /// <summary>Reads one signed 32-bit integer in chart byte order.</summary>
        /// <returns>The integer value.</returns>
        public int ReadInt32()
        {
            var bytes = ReadBytes(sizeof(int));
            return _bigEndian ? BinaryPrimitives.ReadInt32BigEndian(bytes) : BinaryPrimitives.ReadInt32LittleEndian(bytes);
        }

        /// <summary>Reads one IEEE-754 single precision value in chart byte order.</summary>
        /// <returns>The floating-point value.</returns>
        private float ReadSingle() => BitConverter.Int32BitsToSingle(ReadInt32());

        /// <summary>Reads one unsigned 32-bit integer in chart byte order.</summary>
        /// <returns>The integer value.</returns>
        private uint ReadUInt32()
        {
            var bytes = ReadBytes(sizeof(uint));
            return _bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(bytes) : BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        }

        /// <summary>Reads one signed byte.</summary>
        /// <returns>The signed byte.</returns>
        private sbyte ReadSByte() => unchecked((sbyte)ReadBytes(1)[0]);

        /// <summary>Returns a bounded slice and advances the reader.</summary>
        /// <param name="length">The number of bytes to read.</param>
        /// <returns>The requested bytes.</returns>
        private byte[] ReadBytes(int length)
        {
            if (length < 0 || length > _data.Length - _offset)
            {
                throw new InvalidDataException("The SNG chart is truncated or contains an invalid record length.");
            }

            var result = _data.AsSpan(_offset, length).ToArray();
            _offset += length;
            return result;
        }

        /// <summary>Advances past a bounded number of bytes.</summary>
        /// <param name="length">The number of bytes to skip.</param>
        private void Skip(int length)
        {
            if (length < 0 || length > _data.Length - _offset)
            {
                throw new InvalidDataException("The SNG chart is truncated or contains an invalid record length.");
            }

            _offset += length;
        }
    }
}
