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

# Review and optionally delete imported images from latest quality report
./picmag --quality-review /home/user/picture_album --verdict reject --action list
./picmag --quality-review /home/user/picture_album --verdict reject --action delete --apply-changes

# Analyze already imported JPG/JPEG files and persist quality metadata to DB
./picmag --quality-scan-existing /home/user/picture_album --apply-changes

# Schedule periodic imports via user systemd timer (daily at 02:30)
./picmag --schedule-import /home/user/source_collection /home/user/picture_album --period daily --time 02:30

# Schedule periodic imports via user systemd timer (weekly on sunday at 03:00)
./picmag --schedule-import /home/user/source_collection /home/user/picture_album --period weekly --weekday sun --time 03:00

# Remove periodic import schedule (disables timer and removes user unit files)
./picmag --unschedule-import

# Manual review loop with image window + CLI decision (delete/keep/quit)
./picmag --quality-review /home/user/picture_album --verdict reject --action interactive --apply-changes

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
  - Quality analysis metadata is stored per image in the database (`images.quality_*`).
  - `--quality-report` (optional): writes export reports (`.log` and `.json`) to `.picmag/`.
- `--sanity-checks <target path> [extensions] [--dry-run|--apply-changes]`
  - Compares files in target with DB entries.
  - `--dry-run` (default): report only, no DB writes.
  - `--apply-changes`: inserts missing DB entries and removes orphan DB entries.
- `--quality-review <target path> [--verdict review|reject] [--action list|delete|interactive] [--dry-run|--apply-changes]`
  - Uses quality metadata stored in the database (no report file required).
  - Default verdict is `reject`.
  - `--action list` (default): lists matching imported files.
  - `--action delete`: removes matching files and associated DB entries.
  - `--action interactive`: opens each file in a simple viewer window and prompts for `delete` / `keep` / `quit` in CLI.
  - `--dry-run` (default): no file or DB mutation.
  - `--apply-changes`: applies delete action.
- `--quality-scan-existing <target path> [--only-missing|--all] [--dry-run|--apply-changes]`
  - Scans already imported JPG/JPEG files from DB entries and computes quality metadata.
  - `--only-missing` (default): scans only rows without `quality_verdict`.
  - `--all`: rescans all imported JPG/JPEG rows.
  - `--dry-run` (default): performs analysis and writes report without DB updates.
  - `--apply-changes`: writes quality metadata back to DB (`quality_*` columns).
- `--schedule-import <source path> <target path> [extensions] [--delete-source] [--quality-filter off|warn|strict] [--quality-report] --period daily|weekly [--time HH:mm] [--weekday mon..sun]`
  - Creates/updates a user-level `systemd` timer for periodic imports.
  - `--period daily|weekly` is required.
  - `--time HH:mm` is optional (default: `02:00`).
  - `--weekday mon..sun` is used for weekly schedules (default: `mon`).
  - Installs files in `~/.config/systemd/user/picmag-import.service` and `~/.config/systemd/user/picmag-import.timer` and enables the timer using `systemctl --user`.
- `--unschedule-import`
  - Disables/stops periodic import (`systemctl --user disable --now picmag-import.timer`) and removes `~/.config/systemd/user/picmag-import.service` plus `~/.config/systemd/user/picmag-import.timer`.
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

## Bash tab completion

`picmag` supports Bash completion for commands and options (including values for `--quality-filter`, `--verdict`, and `--action`).

If installed via Debian package, the completion file is installed to `/etc/bash_completion.d/picmag`.

Load in current shell:

```bash
source /etc/bash_completion
```

Test:

```bash
picmag --qua<TAB>
picmag --quality-review /path --action <TAB>
```

If completion is not active in your shell startup, add to `~/.bashrc`:

```bash
if [ -f /etc/bash_completion ]; then
  . /etc/bash_completion
fi
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

Build Raspberry Pi packages explicitly:

```bash
# Raspberry Pi OS 64-bit
dotnet deb -r linux-arm64

# Raspberry Pi OS 32-bit (armhf)
dotnet deb -r linux-arm
```

Install on Raspberry Pi:

```bash
sudo apt update
sudo apt install ./path/to/picmag_*_arm64.deb   # or *_armhf.deb
```

If you installed an older package and get `You must install .NET to run this application`, install the runtime once:

```bash
sudo apt update
sudo apt install dotnet-runtime-8.0
```

New runtime-specific packages built with `dotnet deb -r ...` are published as self-contained and do not require a separate .NET runtime installation.

For Raspberry Pi OS 32-bit (armhf), install the armhf package explicitly:

```bash
sudo apt install ./path/to/picmag_*_armhf.deb
```

Alternative (manual):

```bash
sudo dpkg -i ./path/to/picmag_*_arm64.deb   # or *_armhf.deb
sudo apt -f install
```

## Features

- Import images and videos into date-based directories
- Optional source cleanup via `--delete-source`
- Import summary logs with imported / not imported file lists
- Optional JPEG quality analysis with `--quality-filter` (`off|warn|strict`)
- Optional per-file quality report via `--quality-report`
- Post-import review and cleanup via `--quality-review`
- Sanity checks with dry-run (default) and apply mode
- Cache backward compatibility + explicit cache migration command

## Roadmap

- Add more built-in media extensions and metadata extractors
- Improve reporting granularity for large imports
