# psarc2tab

`psarc2tab` is a cross-platform .NET command-line application for converting
Rocksmith-style PSARC song data into guitar tablature. The repository currently
contains the initial project structure and early PSARC inspection support;
conversion is not implemented.

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
psarc2tab --help
psarc2tab --version
```

`--in`/`-in` and `--out`/`-out` may be supplied in either order. The input must
exist and be readable. The output directory must exist and be writable, and an
existing output file is never overwritten. Conversion is not implemented yet;
a valid conversion request currently reports that limitation and exits with
code 5.

### Exit codes

| Code | Meaning |
| ---: | --- |
| 0 | Help or version displayed |
| 2 | Invalid arguments |
| 3 | Input file cannot be read |
| 4 | Output destination cannot be written |
| 5 | Conversion is not implemented |

## Known limitations

- Full archive extraction, Rocksmith song interpretation, and tablature output
  are not implemented.
- Current PSARC support covers header and index inspection plus extraction of
  one entry at a time for the observed PC and Mac archive layout.

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
the Mac index has 129. These checks confirm index metadata only: archive files
are not extracted, and the manifest has not yet been read to resolve path
digests to filenames.

The entry extractor handles raw blocks and zlib-compressed blocks. It processes
one block at a time, validates each block against the declared uncompressed
length, and writes to a caller-provided stream. The manifest entry was
successfully extracted from both supplied examples; mapping manifest names to
the TOC path digests remains future work.
