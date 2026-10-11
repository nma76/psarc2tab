# Rocksmith format facts

This note records observations from sampled PC and Mac archives. The findings
do not establish support for every Rocksmith release or archive.

## Confirmed in both sampled archives

- Each archive has 28 TOC entries. Entry zero is the names manifest; its
  extracted payload contains 27 LF-delimited paths encoded as UTF-8 without a
  byte order mark.
- Manifest line `n` names TOC entry `n + 1`. The MD5 digest of each UTF-8 path
  matches the corresponding entry digest in both archives.
- All 27 entries can be extracted with the current block reader. The data
  includes XML arrangements, JSON manifests, binary SNG charts, audio banks and
  streams, album art, and game metadata.
- Arrangement XML and JSON payloads are valid UTF-8 documents. The arrangement
  files parse as XML; the per-arrangement manifest files parse as JSON objects
  with `Entries`, `ModelName`, `IterationVersion`, and `InsertRoot` properties.
- Bass, lead, and rhythm XML use a `song` root. Their root contains metadata,
  tuning, phrases, beats, sections, events, and difficulty levels.
  Level data includes notes, chords, fret-hand mutes, anchors, and hand shapes.
  Note elements expose fields including `time`, `fret`, `string`, and `sustain`.
- In a sampled Lead arrangement, note `time` and `sustain` values are
  decimal seconds (for example, `time="7.024"` and `sustain="0.146"`). The
  importer preserves them as `TimeSpan` values.
- Instrument note `string` values use zero-based indices in the XML. The
  importer maps them to the Domain model's one-based string numbers.
- Arrangement tuning contains six `string0` through `string5` semitone offsets.
  The importer applies these to standard guitar MIDI pitches
  `[40, 45, 50, 55, 59, 64]`; Bass uses the same pitches one octave lower.
- Chord events refer to root-level templates by `chordId`. The importer expands
  nonnegative template frets into chord notes. XML chord events provide a start
  time but no duration, so imported chord durations are zero rather than
  inferred.
- The importer selects the highest difficulty level and skips notes marked
  `ignore`. These are explicit conversion policies, not claims about every
  Rocksmith consumer's presentation of levels or ignored events.
- Vocal arrangement XML uses a `vocals` root; show-light data uses a
  `showlights` root.
- The bass, lead, rhythm, and show-light XML payloads are byte-identical between
  the paired PC and Mac samples. The vocal XML payload differs between them.
- The PC archive uses `songs/bin/generic` and `audio/windows` paths. The Mac
  archive uses `songs/bin/macos` and `audio/mac` paths. Its SNG files also have
  different lengths from their corresponding PC entries.
- In these archives, blocks recorded with zero size are raw. Blocks whose
  recorded size equals their expected uncompressed length are also raw; the
  shorter blocks are zlib streams. This allowed every entry in both examples
  to be extracted.

## Import scope and remaining unknowns

- SNG chart sections and their note, chord, timing, tuning, and string-count
  semantics. Container decoding is implemented, but the binary chart has not
  yet been mapped to the Domain model.
- The interpretation of nonzero song `offset` values and whether additional
  timing adjustments are needed for other chart variants. The first importer
  accepts an absent or zero offset and rejects nonzero offsets.
- How phrase iterations, sections, anchors, hand shapes, fret-hand mutes, and
  every technique field should be represented in the Domain model. The first
  importer handles note sustains, common note techniques, and chord templates;
  it does not import those other structures.
- Why the vocal XML differs between these platform variants and whether other
  versions use the same layout.
- Whether these structures and compression behaviors apply to archives beyond
  the sampled archives.

Treat the confirmed items above as observations for these samples. Resolve the
unknowns with additional focused fixtures before relying on them in conversion.

## SNG-based PC samples

The sampled SNG-based PC archives use a different arrangement layout from the
paired PC/Mac samples described above:

- Each contains Bass, Lead, and Rhythm charts under `songs/bin/generic/` as
  `.sng` files. It has no instrument chart XML entries under `songs/arr/`; the
  only XML entry under that directory is show-light data.
- Per-arrangement JSON manifests under `manifests/songs_dlc_*/` identify the
  arrangement through `ArrangementName` and refer to an XML asset name through
  `SongXml`. That asset name correlates with the basename of the matching SNG
  chart.
- Bass is represented by `ArrangementName: Bass` and `ArrangementType: 3` in
  these samples. Those fields identify the instrument category, not its
  physical string count.
- Every arrangement JSON manifest has six `Tuning` offsets (`string0` through
  `string5`). This demonstrates a six-position metadata layout, not that every
  bass arrangement uses six strings. It cannot resolve the actual string-count
  question.
- The SNG payloads start with an eight-byte header, followed by a 16-byte
  counter, encrypted data, and a 56-byte trailing signature. The encrypted
  data contains a 32-bit decoded-length field and a zlib stream. The PC samples
  use the little-endian marker `0x4A, 3`; the reader validates that marker and
  checks the decoded size against a 64 MiB limit.
- The reader selects PC or Mac byte order and key from `songs/bin/generic/` or
  `songs/bin/macos/`. All nine Bass, Lead, and Rhythm SNG charts in the three
  sampled PC archives decoded successfully. Synthetic tests cover both platform
  variants; no SNG-based Mac archive is currently available for real-file
  verification. The trailing signature is skipped, not cryptographically
  validated.

The arrangement catalog lists these JSON/SNG arrangements and extracts the SNG
payload. The SNG reader decodes and imports charts from all three observed PC
archives. It selects the highest difficulty level, maps individual notes and
chords through their templates, and reads event time and sustain values in
seconds. Common technique bits map to the Domain model. Tuning is calculated
from the six manifest offsets and the standard guitar or bass MIDI pitches.
Source string indices are checked against those six available positions.

Chart metadata includes six tuning positions. This does not reliably establish
how many physical strings an arrangement uses, so the current tablature output
keeps six rows. Deciding whether bass output should use four, five, or six rows
remains open. The chart reader accounts for the observed difficulty, note,
chord-template, NLD, action, event, tone, DNA, and section layouts. XML section
headings are imported and displayed in tablature; the binary SNG section
records are still skipped. No SNG-based Mac archive is available for
end-to-end verification.
