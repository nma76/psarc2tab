# psarc2tab

`psarc2tab` is a cross-platform .NET command-line application for converting
Rocksmith-style PSARC song data into guitar tablature. The repository contains
PSARC extraction support, a Rocksmith arrangement XML importer, a
format-independent Domain model, and a basic text tab generator. The CLI can
convert a selected Lead, Bass, or Rhythm arrangement to the version 1 `.tab`
format.

## Requirements

- .NET 10 SDK

## Build

```sh
dotnet build Psarc2Tab.slnx
```

## Test

```sh
dotnet test Psarc2Tab.slnx
```

## Usage

The CLI accepts:

```sh
psarc2tab --in <input.psarc> --out <output.tab>
psarc2tab -in <input.psarc> -out <output.tab>
psarc2tab --in <input.psarc> --list
psarc2tab --in <input.psarc> --out <output.tab> --arrangement <number>
psarc2tab --help
psarc2tab --version
```

`--in`/`-in` and `--out`/`-out` may be supplied in either order. The input must
exist and be readable. The output directory must exist and be writable, and an
existing output file is never overwritten. Output is written to a temporary
file in the destination directory and moved into place only after writing
completes. `--list` extracts the manifest and displays numbered Lead, Bass,
and Rhythm XML arrangements found under `songs/arr/`. Use the displayed number
with `--arrangement` to select one; it defaults to the first listed
arrangement when omitted. A successful conversion prints its destination to
stdout.

### Exit codes

| Code | Meaning |
| ---: | --- |
| 0 | Help, version, or conversion completed |
| 2 | Invalid arguments |
| 3 | Input file cannot be read |
| 4 | Output destination cannot be written |
| 5 | Arrangement could not be exported |
| 6 | The selected arrangement number is unavailable |

## Known limitations

- The importer selects the highest difficulty and skips notes marked `ignore`.
  It does not import phrase structure, sections, anchors, or hand shapes.
  Nonzero song offsets are rejected while their meaning is unverified. Observed
  chord events have no duration field and are imported with zero duration.
- Arrangement XML whose declared instrument does not match its selected archive
  entry is rejected.
- The Domain model represents songs, tracks, string tunings, timed notes and
  chords, fretted positions, and a first set of note techniques. Timeline values
  use `TimeSpan`; see the format facts document for the supported XML mapping.
- Current PSARC support covers header and index inspection plus extraction of
  one entry at a time for the observed PC and Mac archive layout. The CLI can
  resolve Lead, Bass, and Rhythm arrangement paths from the extracted manifest.

## PSARC header and index inspection

The header reader currently recognizes the common 32-byte header layout in
the supplied PC and Mac example archives. Both report version 1.4, zlib
compression, 30-byte TOC entries, 28 entries, 65,536-byte blocks, and raw
archive flags `0x4`. Their TOC sizes differ: 1,128 bytes for PC and 1,130 bytes
for Mac. The header reader supports raw flags `0x0` and `0x4`. Flag `0x4` marks
an encrypted TOC; the index reader decrypts it with AES-256 CFB and reads each
30-byte record (path digest, first block index, 40-bit uncompressed length,
and 40-bit archive-relative data offset) followed by 16-bit block sizes.
The index reader validates record bounds and limits the TOC payload to 64 MiB.
Both supplied examples have 28 entries; the PC index has 128 block sizes and
the Mac index has 129. The CLI extracts the manifest entry and uses its
verified line-to-index ordering to resolve archive paths. `--list` displays the
Lead, Bass, and Rhythm XML arrangements; vocals and show-light XML are not
listed as tablature instruments.

The entry extractor handles raw blocks (both zero-size markers and recorded
sizes equal to the expected uncompressed block length) and zlib-compressed
blocks. It processes one block at a time, validates each block against the
declared uncompressed length, and writes to a caller-provided stream. The
manifest entry was extracted from both supplied examples and the CLI lists
their instrument arrangements. Arrangement numbering follows each archive's
manifest order, so the same instrument can have a different number in a PC
and Mac archive.

See [Rocksmith format facts](docs/rocksmith-format-facts.md) for confirmed
observations from the supplied PC and Mac examples and details that remain
unknown.

## Tab file format v1

The initial output format is a versioned, tab-separated event list. It is
designed to preserve exact `TimeSpan` values without assuming a tempo, meter,
or beat grid. A file begins with `# psarc2tab-tab/1`, followed by JSON-escaped
song, artist, and track metadata, and the open-string MIDI pitches in
string-number order. Event rows use these columns:

```text
position_ms  string  fret  duration_ms  event  techniques  chord_label
```

Times are decimal milliseconds with up to four fractional digits. String
numbers are one-based in tuning order; fret zero means an open string. Chords
are written as one row per fretted string with the shared position, duration,
and label. Rows are sorted by position and string number. Lines end with LF for
consistent output across operating systems.
