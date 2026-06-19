# picmag - picture manager (standard)

Command-line tool to organize photos and videos into a chronological folder structure.

## Quickstart

```bash
# Build
dotnet build

# Import JPG + MP4 files into a managed target folder
./picmag -i /home/user/source_collection /home/user/picture_album

# Check consistency between filesystem and DB (default: dry-run)
./picmag --sanity-checks /home/user/picture_album

# Persist quality metadata for already imported files
./picmag --quality-scan-existing /home/user/picture_album --apply-changes

# Review rejected files from DB quality metadata
./picmag --quality-review /home/user/picture_album --verdict reject --action list
```

## Requirements

- .NET SDK 8.0
- libsqlite3-dev
- ffprobe (optional, used for MP4 metadata timestamp extraction)

## Build and run

```bash
dotnet build
./picmag -h
./picmag --version
```

## Common commands

- `-i <source path> <target path> [extensions] [--delete-source]`
- `--sanity-checks <target path> [extensions] [--dry-run|--apply-changes]`
- `--quality-review <target path> [--verdict review|reject] [--action list|delete|interactive] [--dry-run|--apply-changes]`
- `--quality-scan-existing <target path> [--only-missing|--all] [--dry-run|--apply-changes]`
- `--person-scan-existing <target path> [--only-missing|--all]`
- `--person-add <target path> <name>`
- `--person-list <target path>`
- `--person-label <target path> [--limit N] | --face-id <id> (--person <name>|--reject)`
- `--person-search <target path> <name>`
- `--person-train <target path>`
- `--person-predict <target path> [--limit <n>] [--min-confidence <0.0-1.0>]`
- `--person-review <target path> <prediction id> (--accept|--reject)`
- `--schedule-import <source path> <target path> ...`
- `--unschedule-import`
- `--migrate-cache <target path>`

## Documentation

- CLI details: [docs/cli.md](docs/cli.md)
- Person recognition workflow: [docs/person-recognition.md](docs/person-recognition.md)
- Debian and Raspberry Pi packaging: [docs/packaging.md](docs/packaging.md)
- Bash completion setup: [docs/shell-completion.md](docs/shell-completion.md)
- Versioning policy: [VERSIONING.md](VERSIONING.md)

## Tests

```bash
# Integration tests
./picmag/tests/integration_test.sh

# Unit tests
dotnet test ./unittests/unittests.csproj
```

## Notes

- `--delete-source` is opt-in and deletes source files only after successful copy + DB insert.
- Build may show `NU1701` warnings due to legacy SQLite package compatibility metadata.