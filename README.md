# psarc2tab

`psarc2tab` is a cross-platform .NET command-line application for converting
Rocksmith-style PSARC song data into guitar tablature. The repository contains
PSARC extraction support and a format-independent Domain model. Rocksmith
interpretation and tablature conversion are not implemented yet.

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
existing output file is never overwritten. Conversion is not implemented yet;
a valid conversion request currently reports that limitation and exits with
code 5. `--list` extracts the manifest and displays numbered Lead, Bass, and
Rhythm XML arrangements found under `songs/arr/`. Use the displayed number with
`--arrangement` to select one; the number is validated against the archive.
Arrangement selection is ready in the CLI, while tablature export is not yet
implemented.

### Exit codes

| Code | Meaning |
| ---: | --- |
| 0 | Help or version displayed |
| 2 | Invalid arguments |
| 3 | Input file cannot be read |
| 4 | Output destination cannot be written |
| 5 | Conversion is not implemented |
| 6 | The selected arrangement number is unavailable |

## Known limitations

- Rocksmith song interpretation and tablature output are not implemented.
- The Domain model represents songs, tracks, string tunings, timed notes and
  chords, fretted positions, and a first set of note techniques. Timeline values
  use `TimeSpan`; source time units and mapping rules remain unverified.
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
