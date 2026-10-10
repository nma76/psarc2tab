# psarc2tab

`psarc2tab` is a cross-platform .NET command-line application for converting
Rocksmith-style PSARC song data into guitar tablature. The repository currently
contains the initial project structure; conversion and format support have not
yet been implemented.

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

- PSARC parsing, Rocksmith song interpretation, and tablature output are not
  implemented beyond the header reader described below.
- No archive contents or Rocksmith variants have been confirmed as supported.

## PSARC header validation

The header reader currently recognizes the common 32-byte header layout in
the supplied PC and Mac example archives. Both report version 1.4, zlib
compression, 30-byte TOC entries, 28 entries, 65,536-byte blocks, and raw
archive flags `0x4`. Their TOC sizes differ: 1,128 bytes for PC and 1,130 bytes
for Mac. The reader accepts and preserves only the observed flag value; it
does not yet process flag-dependent TOC encryption. This validates only the
header; it does not mean the TOC or archive contents are supported.
