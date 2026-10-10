# AGENTS.md

## Project purpose

Build `psarc2tab`, a cross-platform command-line application that reads
Rocksmith-style PSARC archives, extracts and interprets relevant
song/arrangement data, and writes guitar tablature.

The repository starts empty. Establish the project structure before
implementing features. The application is a CLI only: **do not add a
GUI** unless explicitly requested.

Example intended usage:

``` text
psarc2tab -in song_p.psarc -out song.tab
```

The exact input formats, archive variants, and output tablature syntax
must be verified during development rather than assumed.

## Technology choices

-   Language: C#
-   Runtime and target framework: .NET 10 (`net10.0`)
-   CLI: use a small, maintainable argument-parsing library only if it
    materially improves the CLI; otherwise keep argument handling
    isolated behind a dedicated component.
-   Tests: xUnit
-   Use built-in .NET APIs for binary parsing where appropriate,
    including `Stream`, `Span<byte>`, and
    `System.Buffers.Binary.BinaryPrimitives`.
-   Keep the solution cross-platform across Windows, macOS, and Linux.
-   Use nullable reference types and implicit usings.
-   Treat compiler warnings as important; do not suppress warnings
    without a clear reason.

## Architecture and separation of concerns

Use a modular solution with clear project boundaries. Start with these
projects unless implementation evidence justifies a change:

``` text
src/
  Psarc2Tab.Domain/
  Psarc2Tab.Application/
  Psarc2Tab.Formats.PSARC/
  Psarc2Tab.Formats.Rocksmith/
  Psarc2Tab.Tablature/
  Psarc2Tab.Cli/
tests/
  Psarc2Tab.Domain.Tests/
  Psarc2Tab.PSARC.Tests/
  Psarc2Tab.Rocksmith.Tests/
  Psarc2Tab.Application.Tests/
```

Responsibilities:

-   **Domain**: format-independent musical concepts and invariants, such
    as songs, tracks, tuning, notes, timing, and tablature-related value
    objects. It must not depend on PSARC, Rocksmith file formats, CLI
    libraries, file-system access, or console output.
-   **Application**: orchestrates use cases, coordinates import and
    export abstractions, validates workflow-level requirements, and
    translates failures into meaningful application results. It should
    not parse binary formats directly.
-   **Formats.PSARC**: parses PSARC archive structure, validates archive
    metadata and offsets, and exposes archive entries or streams. It
    must not contain tablature-rendering logic.
-   **Formats.Rocksmith**: interprets relevant files and converts their
    content into the format-independent Domain model. It may use the
    PSARC project to obtain archive entries, but Domain must not depend
    on it.
-   **Tablature**: converts the Domain model into the requested
    tablature representation. It must not know how PSARC archives are
    parsed or how Rocksmith data is stored.
-   **CLI**: parses command-line arguments, invokes Application use
    cases, prints concise diagnostics, and sets the process exit code.
    It must not contain archive parsing, music interpretation, or
    tablature-generation algorithms.

Keep dependencies directed toward abstractions and the Domain model.
Avoid circular project references. Put interfaces in the layer that owns
the abstraction or use case; keep concrete implementations in their
relevant adapter projects.

Do not create interfaces for every class by default. Introduce
abstractions at meaningful boundaries, especially importers, archive
readers, and tablature writers.

## Design principles

-   Use small, cohesive classes and methods with explicit
    responsibilities.
-   Prefer descriptive names and strongly typed models over loosely
    structured dictionaries, magic numbers, or unlabelled tuples.
-   Use enums or value objects for meaningful finite concepts; do not
    use enums for values that are actually open-ended.
-   Keep parsing, validation, domain conversion, and output writing as
    separate stages.
-   Avoid a "god class" or putting application logic in `Program.cs`.
    `Program.cs` should be a thin composition root.
-   Use dependency injection only where it makes composition or testing
    clearer; avoid building a framework inside the application.
-   Make important transformations deterministic and unit-testable.
-   Do not introduce speculative abstractions or unnecessary design
    patterns. Prefer a simple design that can evolve from evidence.

## Code comments

-   Write code comments in concise, clear English.
-   Add XML documentation comments (`///`) to every declared type and member,
    including private helpers and test methods. Document purpose, behavior, and
    relevant constraints; add `<param>`, `<returns>`, and `<exception>` tags
    where they clarify the contract.
-   Keep comments short and useful. Do not repeat a name or narrate an
    implementation that is already obvious. Explain non-obvious behavior at
    the enclosing member rather than adding comments to routine statements.

## Platform compatibility

-   Keep all application code and dependencies OS-agnostic and compatible
    with every operating system supported by the targeted .NET runtime.
-   Prefer cross-platform .NET APIs. Do not rely on operating-system-specific
    libraries, shell commands, native binaries, P/Invoke, registry access, or
    platform-specific services unless a requirement makes them necessary.
-   Use APIs such as `Path`, `File`, and `Directory` for filesystem work.
    Do not assume path separators, drive letters, case sensitivity, newline
    conventions, or filesystem behavior that is specific to one platform.
-   When platform-specific behavior is required, isolate it behind a clear
    boundary, provide implementations for the supported platforms, and test
    or document the supported behavior explicitly.

## Binary-format and reverse-engineering rules

-   Treat input archives as untrusted data.
-   Validate signatures, versions, counts, offsets, lengths, block
    sizes, and index bounds before using them.
-   Detect truncated, malformed, unsupported, or inconsistent input and
    return actionable errors rather than crashing with obscure
    exceptions.
-   Avoid allocating memory directly from untrusted size fields without
    validating limits.
-   Do not assume that every PSARC file uses the same compression,
    encryption, manifest, or block-layout variant. Confirm behavior
    against actual files and reliable format evidence.
-   Separate archive-container parsing from parsing files contained in
    the archive.
-   Document confirmed format details and distinguish them from
    hypotheses.
-   When a behavior is not understood, add a focused experiment or test
    fixture instead of silently guessing.
-   Do not commit copyrighted song archives or other large/private
    source assets. Use minimal, legally appropriate fixtures and
    synthetic byte sequences where possible.

## CLI contract

The initial command shape is:

``` text
psarc2tab -in <input.psarc> -out <output.tab>
```

Requirements:

-   Support the documented `-in` and `-out` options. Any aliases or
    additional options must be deliberate and documented.
-   Show concise usage/help text for `--help` or invalid arguments.
-   Validate that the input exists and is readable, and that the output
    destination can be written.
-   Never overwrite an existing output file silently. Define and
    document an explicit overwrite option if needed.
-   Write output safely: avoid leaving a misleading partial output file
    on failure where practical.
-   Send normal output to stdout and diagnostics to stderr.
-   Use stable, documented non-zero exit codes for failures; do not
    expose stack traces by default.
-   Do not claim successful conversion until the output has been fully
    written.
-   Keep argument parsing separate from the conversion workflow so the
    core can be called and tested without a process invocation.

## Error handling and logging

-   Use exceptions for exceptional failures and handle them at
    appropriate boundaries.
-   Do not catch and discard exceptions.
-   Provide contextual errors such as which archive entry or parsing
    stage failed, without dumping sensitive or excessive binary data.
-   Keep diagnostics useful to a CLI user and avoid noisy output on
    successful runs.
-   Do not add a logging framework unless there is a concrete need.

## Testing

-   Add tests alongside each meaningful capability; do not defer all
    testing until the end.
-   Test valid input, malformed input, boundary values, truncated data,
    unsupported variants, and failure paths.
-   Use synthetic fixtures for binary parsing when possible.
-   Add regression tests for every bug found in a real archive.
-   Test Domain and tablature generation without needing a PSARC file.
-   Test that the same Domain model produces the same output for the
    same settings.
-   Tests must not depend on network access or machine-specific absolute
    paths.
-   Do not report tests as passing unless they were actually run.

## Build and workflow

-   Keep the repository buildable after each meaningful step.
-   Prefer small, reviewable changes that implement one vertical slice
    at a time.
-   Before finishing a change, run the relevant tests and then build the
    solution. Report any command that could not be run.
-   Do not add unrelated dependencies, generated binaries, secrets,
    local IDE state, or user-specific files to version control.
-   Add a `.gitignore` appropriate for .NET.
-   Update the README with setup, build, test, usage, known limitations,
    and confirmed format support as these become available.
-   Do not claim full PSARC or Rocksmith support until the supported
    variants have been tested and documented.

## Suggested first milestones

1.  Create the solution, projects, project references, `.gitignore`, and
    a minimal README.
2.  Establish a working CLI that validates `-in` and `-out`, prints
    help, and returns documented exit codes. Conversion may initially
    report "not implemented."
3.  Implement PSARC header and index inspection with focused tests; do
    not begin with a full conversion pipeline.
4.  Implement safe archive-entry access and test against small fixtures.
5.  Parse one known Rocksmith song/arrangement into the Domain model.
6.  Generate a minimal `.tab` output from a Domain fixture,
    independently of PSARC.
7.  Connect the import and output stages through Application
    orchestration.

Prioritize a small, verifiable end-to-end slice over implementing all
planned projects at once.

## Branch model

- `develop` is the trunk branch. Create feature and bug-fix branches from
  `develop`, and merge completed work back into `develop`.
- Implement each new feature on its own branch named with the `feature/`
  prefix (for example, `feature/psarc-header-reader`).
- Fix bugs in code that has already been merged into `develop` on a new
  branch named with the `bug/` prefix (for example, `bug/index-offset-check`).
- Fix bugs found before a feature branch is merged directly on that feature
  branch.
- The user authorizes Codex to commit and merge completed work into `develop`
  without asking again when the work is ready to be checked off in `TODO.txt`.
- Keep feature and bug-fix work on its topic branch until the relevant TODO
  item is complete, required documentation is updated, and relevant tests and
  the solution build have actually passed. Mark the TODO item complete in the
  topic branch, then commit and merge that branch into `develop`.
- When the user explicitly requests a change directly on `develop`, make the
  change and commit it there once it meets the same completion and verification
  criteria.

## TODO workflow

- Read `TODO.txt` before starting implementation work. Use it to identify
  the next unfinished task and keep work aligned with its dependencies.
- Mark a task complete (`[x]`) only after its implementation and required
  documentation are present and its relevant tests and build have actually
  passed. Do not check off work based only on intent or partial support.
- When implementation reveals a missing prerequisite, follow-up, or newly
  discovered requirement, add it to `TODO.txt` with a clear description.
- Keep `TODO.txt` ordered so that tasks can be implemented in sequence: list
  prerequisites before dependent work, and reorder or split tasks when new
  findings change those dependencies.
- Keep tests and robustness work alongside each task. The robustness item in
  `TODO.txt` tracks broader cross-cutting and regression coverage; it does not
  replace tests required by individual tasks.
