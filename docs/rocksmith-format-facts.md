# Rocksmith format facts

This note records observations from the supplied PC (`_p`) and Mac (`_m`)
archives. They are two platform variants of one song package, so these findings
do not establish support for every Rocksmith release or archive.

## Confirmed in both supplied archives

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
- Bass, lead, and rhythm XML use a `song` root. Their root contains song
  metadata, tuning, phrases, beats, sections, events, and difficulty levels.
  Level data includes notes, chords, fret-hand mutes, anchors, and hand shapes.
  Note elements expose fields including `time`, `fret`, `string`, and `sustain`.
- In the supplied Lead arrangement, note `time` and `sustain` values are
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
  these PC and Mac examples. The vocal XML payload differs between them.
- The PC archive uses `songs/bin/generic` and `audio/windows` paths. The Mac
  archive uses `songs/bin/macos` and `audio/mac` paths. Its SNG files also have
  different lengths from the corresponding PC files.
- In these archives, blocks recorded with zero size are raw. Blocks whose
  recorded size equals their expected uncompressed length are also raw; the
  shorter blocks are zlib streams. This allowed every entry in both examples
  to be extracted.

## Import scope and remaining unknowns

- The SNG payload structure and whether its contents are encrypted or
  transformed. Its observed prefix is not enough to interpret it.
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
  the two supplied examples.

Treat the confirmed items above as observations for these examples. Resolve the
unknowns with additional focused fixtures before relying on them in conversion.

## Additional SNG-based PC examples

The `americanidiot_p.psarc`, `garbithink_p.psarc`, and `kissheavens_p.psarc`
examples use a different arrangement layout from the supplied Hard-to-Handle
PC/Mac pair:

- Each contains Bass, Lead, and Rhythm charts under `songs/bin/generic/` as
  `.sng` files. It has no instrument chart XML entries under `songs/arr/`; the
  only XML entry under that directory is show-light data.
- Per-arrangement JSON manifests under `manifests/songs_dlc_*/` identify the
  arrangement through `ArrangementName` and refer to an XML asset name through
  `SongXml`. That asset name correlates with the basename of the matching SNG
  chart.
- Bass is represented by `ArrangementName: Bass` and `ArrangementType: 3` in
  these examples. Those fields identify the instrument category, not its
  physical string count.
- Every arrangement JSON manifest has six `Tuning` offsets (`string0` through
  `string5`). This demonstrates a six-position metadata layout, not that every
  bass arrangement uses six strings. It cannot resolve the actual string-count
  question.
- The SNG payloads share an eight-byte prefix in these PC examples. Their
  remaining bytes have not been decoded by this project; note, timing, and
  string-index semantics remain unverified here.

The arrangement catalog now lists these JSON/SNG arrangements and extracts the
SNG payload. Conversion remains unsupported until the SNG container and chart
sections are decoded and mapped to the Domain model.
