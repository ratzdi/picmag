# Changelog

All notable changes to this project are documented in this file.

## [Unreleased]

### Added

- Import option `--quality-filter off|warn|strict` for JPEG quality screening during import.
- Import option `--quality-report` to write a detailed quality analysis report to `.picmag/quality-report-<timestamp>.log`.
- Command `--quality-review` for post-import quality review with list/delete actions and dry-run/apply modes.
- Machine-readable quality report output (`.picmag/quality-report-<timestamp>.json`) for automated review workflows.
- Interactive post-import mode (`--quality-review --action interactive`) to open each candidate image and decide via CLI (`delete` / `keep` / `quit`).
- Command `--quality-scan-existing` to analyze already imported JPG/JPEG files and populate DB quality metadata retroactively.
- Unit tests for quality analysis behavior (`QualityAnalyzerTests`).
- Integration test coverage for quality warn mode and report generation.

### Changed

- Import summary now includes quality filter mode and quality counters (reviewed, rejected, analysis errors).
- Quality assessment entries now include source path, target path, and import status for actionable cleanup.
- Quality review now reads quality metadata from DB (`images.quality_*`) instead of requiring JSON reports; JSON remains optional export.
- Existing collections can now be quality-scanned post-import in dry-run or apply mode (`--quality-scan-existing --only-missing|--all`).

### Fixed

-

## [0.2.0] - 2026-02-27

<!-- markdownlint-disable MD024 -->

### Added

- CLI command `--migrate-cache <target path>` to migrate `.picmag/cache.txt` to the current cache format.
- Cache migration backup creation (`.picmag/cache.txt.bak`) before replacing the cache file.
- Integration test coverage for cache migration from legacy cache format.
- Unit test coverage for `ImagesTable` (`ImageExists`, `FindDuplicates`, `RemoveByPath`).

### Changed

- Standalone sanity checks now support explicit modes: `--dry-run` (default) and `--apply-changes`.
- Sanity check report now includes mode and mutation counters (`inserted_db_entries_count`, `removed_db_entries_count`).
- Sanity check internals now support optional DB synchronization when apply mode is enabled.
- Cache reader now detects and reads legacy cache lines while logging legacy entry count.
- README reorganized and expanded (Quickstart, CLI reference, requirements, tests, features/roadmap).
- CLI architecture refactored: argument parsing and command execution split into dedicated partial files (`Program.CommandParser.cs`, `Program.CommandHandlers.cs`).
- Import pipeline refactored to `BlockingCollection` with completion signaling (no busy-wait polling loop).
- File logging switched to deterministic thread-safe writes.
- `Plugin.SQLiteConnection` package reference removed from the main project.

### Fixed

- Dry-run sanity checks no longer mutate the database.
- `ImagesTable` queries now use parameterized SQL for safer path/hash handling.

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

<!-- markdownlint-enable MD024 -->

