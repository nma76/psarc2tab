# psarc2tab

[![CI](https://github.com/nma76/psarc2tab/actions/workflows/ci.yml/badge.svg?branch=develop)](https://github.com/nma76/psarc2tab/actions/workflows/ci.yml)
[![Release](https://github.com/nma76/psarc2tab/actions/workflows/release.yml/badge.svg)](https://github.com/nma76/psarc2tab/actions/workflows/release.yml)
[![Downloads](https://img.shields.io/github/downloads/nma76/psarc2tab/latest/total)](https://github.com/nma76/psarc2tab/releases/latest)

## From Rocksmith archive to practice-ready tab

Turn a supported Rocksmith arrangement into tablature you can read, practice
from, and share. `psarc2tab` extracts Lead, Rhythm, or Bass parts from PSARC
archives and renders them as classic ASCII tabs, with tuning and song sections
alongside the notes. List the arrangements before you choose, or export timed
lyrics as an LRC file. It runs as a cross-platform command-line tool on Windows,
macOS, and Linux.

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

## CI and releases

Pushes to `develop` run the Release build and full test suite. Pushing a stable
semantic-version tag such as `v1.2.3` runs the same checks, then creates
self-contained single-file CLI packages for `win-x64`, `osx-arm64`, `linux-x64`,
and `linux-arm64`. Release tags must point to a commit in `develop`. Each
platform package is attached to a GitHub Release along with `SHA256SUMS.txt`.

## Usage

The CLI accepts:

```sh
psarc2tab --in <input.psarc> --out <output.tab>
psarc2tab --in <input.psarc> --list
psarc2tab --in <input.psarc> --stats
psarc2tab --in <input.psarc> --stats --arr <number|all>
psarc2tab --in <input.psarc> --out <output.tab> --arr <number>
psarc2tab --in <input.psarc> --out <output.tab> --arr all
psarc2tab --in <input.psarc> --out <song.mid> --format midi --arr all
psarc2tab --in <input.psarc> --out <output.tab> --format tab
psarc2tab --in <input.psarc> --out <output.mid> --format midi
psarc2tab --in <input.psarc> --out <output.txt> --format heatmap --arr <number>
psarc2tab --in <input.psarc> --out <lyrics.lrc> --arr <lyrics-number>
psarc2tab --help
psarc2tab --version
```

`--in` and `--out` may be supplied in either order. All options use double-dash
syntax. The input must exist and be readable. The output directory must exist
and be writable, and an existing output file is never overwritten. Output is
written to a temporary file in the destination directory and moved into place
only after writing completes. `--list` extracts the archive manifest and
displays numbered Lead, Bass, and Rhythm arrangements, followed by Lyrics when a
matching vocal chart is present. It recognizes XML charts under `songs/arr/` and SNG charts
referenced by arrangement JSON manifests. Use the displayed number with
`--arr` to select an item. By default, conversion exports all supported Lead,
Bass, and Rhythm arrangements; tab files receive instrument-specific suffixes.
Use `--arr <number>` to export one item. Selecting a Lyrics number writes
timestamped plain-text LRC output instead of tablature. `--stats` prints archive
details and statistics for every instrument arrangement without writing a
file. Add `--arr <number>` to show
statistics for one numbered arrangement; selecting the Lyrics number reports
that instrument statistics do not apply. `--arr all` is equivalent to omitting
`--arr`. Song title, artist, album, and year are shown once using metadata from
the first instrument arrangement. Rocksmith XML can contain album name and
year, while SNG JSON metadata may omit them; missing values display as
`unavailable`. With statistics, note and technique counts include individual
note events, while chord tones contribute to used
strings and fret range. Length is the latest imported event end, or onset when
the source duration is zero. Platform is inferred only when platform-specific
SNG paths identify PC or Mac. `--format` selects the
arrangement output format and defaults to
`tab`; supported arrangement formats are `tab`, `midi`, and the beta ASCII
`heatmap`. MIDI export writes notes and chord tones from the selected
arrangement(s) to a Standard MIDI File.
It uses 960 ticks per quarter note and derives tempo changes from Rocksmith XML
`ebeats` or decoded SNG beat times;
when fewer than two beat markers are available, it falls back to 120 BPM and
converts absolute seconds to ticks.
A song-level cent offset is sent as channel pitch bend. No time-signature
metadata is emitted because the current domain model does not contain it. Lead
and Rhythm use the General MIDI clean electric guitar program; Bass uses
fingered electric bass. Note articulations and other playing techniques are
not encoded, and playback sound depends on the receiving sequencer or
synthesizer. Positive source sustain durations are preserved through tempo
changes. Events without a duration ring until the next onset anywhere in the
arrangement; an event with no later onset uses a one-tick minimum. Duration
inference and playback have not yet been compared with Rocksmith, GarageBand,
or Windows Media Player. Lyrics remain LRC exports. Unknown formats are
rejected with a list of available formats.

With the default `tab` format, every instrument arrangement is exported as a
separate file; Lyrics entries are excluded. The requested output filename is a
base name, so `mysong.tab` produces files such as `mysong-lead.tab` and
`mysong-bass.tab`. Repeated arrangements of the same instrument receive
numbered suffixes such as `mysong-lead-2.tab`. `--arr all` may be supplied
explicitly for the same behavior. With `--format midi`, the default selection
writes one Standard MIDI type-one file to the
requested output path. It contains a conductor track with the shared tempo map,
followed by one named track per arrangement in archive order. Each arrangement
uses its own melodic MIDI channel so overlapping parts do not share program or
pitch-bend state; the percussion channel is left unused. Combined export
requires every arrangement to have identical beat timestamps, and supports up
to 15 arrangements so each can use an independent melodic channel. Arrangements
with different beat timestamps can be exported separately. Only the
supported, named Lead, Rhythm, and Bass arrangements shown by `--list` are
included; Lyrics and other unrecognized archive entries are not exported. All
destination paths are checked before export, existing files are never
overwritten, and staged files are removed if any import or write fails. A
successful export prints each destination to stdout. For a heatmap export,
choose a `.txt` output name; when all arrangements are selected, each heatmap
receives an instrument suffix like other single-arrangement formats.

### Exit codes

| Code | Meaning |
| ---: | --- |
| 0 | Help, version, or export completed |
| 2 | Invalid arguments |
| 3 | Input file cannot be read |
| 4 | Output destination cannot be written |
| 5 | Arrangement or lyrics could not be exported |
| 6 | The selected archive item number is unavailable |

## Known limitations

- XML and SNG imports select the full difficulty independently for each phrase
  iteration. Both skip notes marked
  `ignore` and import section headings, but not phrase structure, anchors, or
  hand shapes. Section headings are omitted when no note or chord starts in
  their time interval; names such as `NoGuitar1` alone never suppress content.
  Nonzero song offsets are rejected while their meaning is unverified. Observed
  chord events have no duration field and are imported with zero duration.
- Arrangement XML whose declared instrument does not match its selected archive
  entry is rejected.
- The Domain model represents songs, tracks, string tunings, timed notes and
  chords, fretted positions, and a first set of note techniques. Timeline values
  use `TimeSpan`; see the format facts document for the supported XML mapping.
- Current PSARC support covers header and index inspection plus extraction of
  one entry at a time for the observed PC and Mac archive layouts. XML charts
  and decoded SNG charts can be converted for the observed PC examples. SNG
  chart import selects each phrase iteration's full difficulty and maps notes,
  chord templates, timing, tuning, supported techniques, and section headings.
  No SNG-based Mac archive has been verified yet.
- Vocal charts are exported as LRC text with line start timestamps. Rocksmith's
  explicit `+` line marker is honored; unmarked lyric fragments are grouped and
  wrapped at 80 characters. LRC files use LF line endings on every platform.
  SNG vocal parsing uses the declared PC/Mac path to
  choose byte order; the Mac lyric record layout has synthetic test coverage
  only. Only the observed `*_vocals` entry naming is recognized.

## PSARC header and index inspection

The header reader currently recognizes the common 32-byte header layout in
the sampled PC and Mac archives. Both report version 1.4, zlib
compression, 30-byte TOC entries, 28 entries, 65,536-byte blocks, and raw
archive flags `0x4`. Their TOC sizes differ: 1,128 bytes for PC and 1,130 bytes
for Mac. The header reader supports raw flags `0x0` and `0x4`. Flag `0x4` marks
an encrypted TOC; the index reader decrypts it with AES-256 CFB and reads each
30-byte record (path digest, first block index, 40-bit uncompressed length,
and 40-bit archive-relative data offset) followed by 16-bit block sizes.
The index reader validates record bounds and limits the TOC payload to 64 MiB.
Both sampled archives have 28 entries; the PC index has 128 block sizes and
the Mac index has 129. The CLI extracts the manifest entry and uses its
verified line-to-index ordering to resolve archive paths. `--list` displays
Lead, Bass, and Rhythm arrangements from either XML charts or JSON/SNG packages;
vocals and show-light data are not listed as tablature instruments.

The entry extractor handles raw blocks (both zero-size markers and recorded
sizes equal to the expected uncompressed block length) and zlib-compressed
blocks. It processes one block at a time, validates each block against the
declared uncompressed length, and writes to a caller-provided stream. The
manifest entry was extracted from both sampled archives and the CLI lists
their instrument arrangements. Arrangement numbering follows each archive's
manifest order, so the same instrument can have a different number in a PC
and Mac archive.

See [Rocksmith format facts](docs/rocksmith-format-facts.md) for confirmed
observations from the sampled PC and Mac archives and details that remain
unknown.

## ASCII tablature output

Output starts with `Artist`, `Song`, and `Tuning` metadata lines. Tuning is
listed from the lowest string to the highest, using note names with MIDI octave
numbers. `Tuning reference` shows the A4 reference frequency calculated from
the arrangement's cent offset relative to 440 Hz. The tablature then uses the
familiar six-line layout, with the highest string on top and the lowest string
on the bottom. String labels are derived
from the track's open-string pitches; fret numbers and chord tones share
aligned horizontal columns. Hammer-ons, pull-offs, bends, slides, vibrato,
harmonics, muted notes, and other currently modeled techniques use compact
ASCII markers.

Horizontal spacing is approximate: one column represents about 125 ms, with
long gaps capped for readability. The current Domain model does not include
measure boundaries, so row-ending `|` characters wrap the display and do not
claim to represent verified musical bars. Song metadata is not embedded in the
tab text. The provided plain-text tab sample shows the target visual style.

## ASCII fretboard heatmap (beta)

Use `--format heatmap` to write a text heatmap for one arrangement or for all
instrument arrangements. Each standalone note and each chord tone adds one use
to its string/fret position. Strings are shown from highest pitch to lowest,
with their string number and open pitch; open-string notes appear at fret 0.
The grid always includes frets 0 through 24 so separate heatmaps share the
same baseline, and expands up to the highest fret used (maximum 48). Charts
using a higher fret are rejected with an explanation instead of producing an
unbounded output grid.

Heatmap symbols use fixed absolute count ranges across files: blank for zero,
`.` for one use, `:` for 2–3, `+` for 4–7, `#` for 8–15, and `@` for 16 or more.
The output includes a legend and the highest count at any position. This first
beta view compares position use by count; it does not represent timing, note
duration, or a recommended fingering.
