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

The intended command shape is:

```sh
psarc2tab -in <input.psarc> -out <output.tab>
```

The CLI workflow is not implemented yet.

## Known limitations

- PSARC parsing, Rocksmith song interpretation, and tablature output are not
  implemented.
- No PSARC or Rocksmith variants have been confirmed as supported.
