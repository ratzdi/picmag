# picmag - quick start

picmag is a command-line tool to import and organize photos/videos into a date-based folder structure.

## 60-second start

```bash
# 1) Build
dotnet build

# 2) Show help
./picmag -h

# 3) Import JPG + MP4 into your target library
./picmag -i /home/user/source_collection /home/user/picture_album
```

## Most used commands

```bash
# Verify DB/filesystem consistency (safe dry-run by default)
./picmag --sanity-checks /home/user/picture_album

# Scan existing media for quality metadata and persist changes
./picmag --quality-scan-existing /home/user/picture_album --apply-changes

# List rejected files
./picmag --quality-review /home/user/picture_album --verdict reject --action list
```

## Requirements

- .NET SDK 8.0
- libsqlite3-dev
- ffprobe (optional, for MP4 timestamp extraction)

## More docs

- Standard README (full command overview): [docs/readme-standard.md](docs/readme-standard.md)
- CLI reference: [docs/cli.md](docs/cli.md)
- Person recognition workflow: [docs/person-recognition.md](docs/person-recognition.md)
- Packaging (Debian/Raspberry Pi): [docs/packaging.md](docs/packaging.md)
- Shell completion: [docs/shell-completion.md](docs/shell-completion.md)
- Model license status: [docs/model-licenses.md](docs/model-licenses.md)
- Versioning policy: [VERSIONING.md](VERSIONING.md)

## Tests

```bash
./picmag/tests/integration_test.sh
dotnet test ./unittests/unittests.csproj
```
