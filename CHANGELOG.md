# Changelog

This changelog records user-facing changes for each repository release.
Entries are listed from newest to oldest and describe user-facing changes since
the preceding version tag.

## [Unreleased]

### Changed

- Export all supported instrument arrangements by default; use `--arr <number>`
  to export only one arrangement.
- Present MIDI as a supported output format without a preview label.
- Select the full Rocksmith difficulty separately for each XML phrase iteration,
  preserving notes and chords across the complete song.

## [1.5.0] - 2026-10-21

### Added

- Export all supported Lead, Rhythm, and Bass arrangements from an archive to
  one multi-track Standard MIDI File with a separate track and MIDI channel for
  each arrangement.
- Import tempo grids from Rocksmith XML `<ebeats>`, including measure markers
  represented by `measure="-1"` or an omitted `measure` attribute.

### Fixed

- Derive MIDI tempo changes from XML beat timestamps instead of falling back to
  120 BPM when an arrangement uses `<ebeats>`.
- Allow combined MIDI export when arrangements have the same beat timestamps
  but different measure or beat coordinates.

### Documentation

- Document the observed XML and SNG beat formats and the requirement that
  combined MIDI arrangements share the same beat timestamps.

### Limitations

- Combined MIDI export requires a shared beat timeline and supports up to 15
  arrangements. Arrangements with different beat timestamps can be exported
  separately.

## [1.3.5] - 2026-10-20

### Added

- Show archive and arrangement statistics, including song metadata, tuning,
  note counts, techniques, fret range, and approximate duration.
- Display song title and artist once for the archive when showing statistics
  for multiple arrangements.

### Fixed

- Accept statistics input when XML notes omit their `sustain` value.
- Recognize the supported Rhythm arrangement whose internal chart name differs
  from its displayed instrument name.

## [1.3.0] - 2026-10-18

### Added

- Export arrangements as Standard MIDI files with notes, chord tones,
  tuning-based pitches, beat-derived tempo, and song pitch offset.
- Export all supported instrument arrangements from an archive as separate
  output files with instrument-specific filenames.
- Choose the output format with `--format`, while keeping tablature as the
  default.

## [1.2.0] - 2026-10-16

### Improved

- Show the tuning reference pitch in tablature output when the song uses a
  pitch offset.
- Render bass tablature with at least four string lines.
- Omit empty sections from tablature output.

## [1.1.0] - 2026-10-12

### Added

- Export Rocksmith lyrics as timestamped LRC files.
- Select arrangements using the `--arr` option.

## [1.0.0] - 2026-10-11

### Added

- Convert supported Lead, Rhythm, and Bass arrangements from Rocksmith PSARC
  archives into ASCII tablature.
- List available arrangements and select one for export.
- Read both XML-based arrangements and supported SNG-based arrangements.

