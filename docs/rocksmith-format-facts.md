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

## Still unknown

- The SNG payload structure and whether its contents are encrypted or
  transformed. Its observed prefix is not enough to interpret it.
- The units and indexing rules for note time, string numbers, tuning values,
  and other note fields.
- How to map XML phrase iterations, difficulty levels, chord templates, and
  techniques into the format-independent Domain model.
- Why the vocal XML differs between these platform variants and whether other
  versions use the same layout.
- Whether these structures and compression behaviors apply to archives beyond
  the two supplied examples.

Treat the confirmed items above as observations for these examples. Resolve the
unknowns with additional focused fixtures before relying on them in conversion.
