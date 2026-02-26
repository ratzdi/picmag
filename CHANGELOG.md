# Changelog

All notable changes to this project are documented in this file.

## [Unreleased]

### Added

-

### Changed

-

### Fixed

-

## [0.1.0] - 2026-02-26

### Added

- Optional import flag `--delete-source` to remove source files only after successful import.
- CLI command `--version` (and `-v`) to print application version and git short revision.
- Integration tests for `--delete-source` positive and negative scenarios.
- Project versioning guide in `VERSIONING.md`.

### Changed

- Import workflow now logs source deletion counters (`deleted` and `delete failures`).
- CLI help updated with `--delete-source` safety warning and version command.
- Project build metadata updated in `picmag/picmag.csproj` for reliable runtime version output.
- README updated with safety behavior, versioning reference, and `--version` usage.

### Fixed

- Source files remain untouched when no files are importable, even if `--delete-source` is set.

