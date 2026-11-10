# Changelog

This changelog records user-facing changes for each repository release.
Entries are listed from newest to oldest and describe user-facing changes since
the preceding version tag.

## [Unreleased]

### Added

- Add beta export of supported single-song PSARC archives as FeedBack-compatible
  `.feedpak` packages containing arrangement data, a shared timeline, optional
  lyrics, and a decoded WAV mix.

### Requirements and limitations

- Feedpak export uses an external `vgmstream-cli` build with Wwise Vorbis
  support; the executable is not bundled. Multi-song archives and source capo
  metadata are not supported by this initial exporter.

## [1.8.0] - 2026-11-06

### Added

- Batch-export matching PSARC archives from a directory to separate tablature
  files, with deterministic ordering and output-collision checks.

## [1.7.0] - 2026-10-28

### Added

- Optionally add a synthetic percussion track to MIDI exports, using a simple
  4/4 beat pattern. The feature is marked beta and does not reproduce the
  source song's actual drum performance.

## [1.6.0] - 2026-10-23

### Added

- Preview arrangement fretboard activity as a text heatmap, with per-fret
  occurrence shading and optional terminal color.

### Limitations

- The heatmap is a beta text view. Its standard fret range is 0–24 and expands
  only when the arrangement contains higher frets.

## [1.5.1] - 2026-10-21

### Changed

- Export all supported instrument arrangements by default; use `--arr <number>`
  to export only one arrangement.
- Present MIDI as a supported output format without a preview label.

### Fixed

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

