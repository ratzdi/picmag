# picmag - picture manager

Command-line tool to organize photos and videos into a chronological folder structure.

## Quickstart

If you have an unordered image/video collection, `picmag` scans it and imports supported files into a tidy date-based structure.

```bash
# Build
dotnet build

# Import JPG + MP4 files into a managed target folder
./picmag -i /home/user/source_collection /home/user/picture_album

# Optional: delete source files only after successful import
./picmag -i /home/user/source_collection /home/user/picture_album --delete-source

# Check consistency between filesystem and DB (default: dry-run)
./picmag --sanity-checks /home/user/picture_album

# Apply sanity-check changes to sync DB <-> filesystem
./picmag --sanity-checks /home/user/picture_album --apply-changes

# Import with quality analysis (warn mode keeps files, strict skips hard fails)
./picmag -i /home/user/source_collection /home/user/picture_album --quality-filter warn --quality-report

# Migrate legacy cache format to current format (.bak backup is created)
./picmag --migrate-cache /home/user/picture_album
```

## CLI reference

- `-i <source path> <target path> [extensions] [--delete-source]`
  - Imports files from source to target.
  - Default extensions: `jpg,mp4`
  - Example custom extensions: `./picmag -i /src /dst jpg,png`
  - `--quality-filter off|warn|strict`:
    - `off` (default): no quality checks.
    - `warn`: quality issues are reported, files are still imported.
    - `strict`: hard quality failures are not imported.
  - `--quality-report`: writes a detailed per-file quality report to `.picmag/quality-report-<timestamp>.log`.
- `--sanity-checks <target path> [extensions] [--dry-run|--apply-changes]`
  - Compares files in target with DB entries.
  - `--dry-run` (default): report only, no DB writes.
  - `--apply-changes`: inserts missing DB entries and removes orphan DB entries.
- `--migrate-cache <target path>`
  - Rewrites `.picmag/cache.txt` to current format.
  - Creates `.picmag/cache.txt.bak` before replacing.
- `--version` / `-v`
  - Prints app version and git short revision.
- `-h`
  - Shows usage.

## Safety behavior of `--delete-source`

- Deletion is opt-in only. Without `--delete-source`, source files are never deleted.
- A source file is deleted only after successful copy and successful DB insert.
- Files not imported (unsupported extension, duplicate, existing target, etc.) are never deleted.
- If deletion fails, import remains successful and the deletion failure is logged.
- If no files are importable, no source files are deleted.

## Requirements

- `.NET SDK 8.0`
- `libsqlite3-dev`
- `ffprobe` (optional, used for MP4 metadata timestamp extraction)

## Build

```bash
dotnet build
```

## Run

```bash
./picmag -h
./picmag --version
```

## Tests

```bash
# Integration tests
./picmag/tests/integration_test.sh

# Unit tests
dotnet test ./unittests/unittests.csproj
```

## Architecture notes

- CLI is split into parser and command handlers for easier maintenance:
  - `picmag/src/Program.CommandParser.cs`
  - `picmag/src/Program.CommandHandlers.cs`
- Import processing uses a producer/consumer pipeline (`BlockingCollection`) with completion signaling.
- Database access in `ImagesTable` uses parameterized SQL.
- File logging uses thread-safe synchronous append to avoid dropped lines.

## Known warnings

- Build may show `NU1701` warnings due to legacy SQLite package compatibility metadata.
- Current test/build status is green despite these warnings.

## Versioning

See [VERSIONING.md](VERSIONING.md) for the Semantic Versioning policy and release process.

## Debian package

Install `dotnet-deb` first:

```bash
cd picmag
dotnet tool install --global dotnet-deb
dotnet deb install
```

Build package:

```bash
dotnet deb
```

## Features

- Import images and videos into date-based directories
- Optional source cleanup via `--delete-source`
- Import summary logs with imported / not imported file lists
- Optional JPEG quality analysis with `--quality-filter` (`off|warn|strict`)
- Optional per-file quality report via `--quality-report`
- Sanity checks with dry-run (default) and apply mode
- Cache backward compatibility + explicit cache migration command

## Roadmap

- Add more built-in media extensions and metadata extractors
- Improve reporting granularity for large imports
