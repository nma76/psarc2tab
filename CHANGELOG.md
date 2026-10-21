# Changelog

This changelog records user-facing changes for each repository release.
The first entry covers changes since `v1.3.5`.

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
- MIDI export remains beta/experimental.

