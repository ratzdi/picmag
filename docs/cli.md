# CLI Reference

## Core import and maintenance

- `-i <source path> <target path> [extensions] [--delete-source] [--quality-filter off|warn|strict] [--quality-report]`
  - Imports files from source to target.
  - Default extensions: `jpg,mp4`
  - Custom example: `./picmag -i /src /dst jpg,png`
  - Quality metadata is stored in DB columns `images.quality_*`.

- `--sanity-checks <target path> [extensions] [--dry-run|--apply-changes]`
  - Compares target filesystem with DB entries.
  - `--dry-run` default, `--apply-changes` mutates DB.

- `--quality-review <target path> [--verdict review|reject] [--action list|delete|interactive] [--dry-run|--apply-changes]`
  - Uses quality metadata from DB (no report file required).
  - Default verdict: `reject`.

- `--quality-scan-existing <target path> [--only-missing|--all] [--dry-run|--apply-changes]`
  - Scans already imported JPG/JPEG DB rows and computes quality metadata.
  - `--only-missing` default.

- `--migrate-cache <target path>`
  - Rewrites `.picmag/cache.txt` to current format.
  - Creates `.bak` backup.

## Person recognition commands

- `--person-scan-existing <target path> [--only-missing|--all]`
- `--person-add <target path> <name>`
- `--person-list <target path>`
- `--person-label <target path> [--limit N] | --face-id <id> (--person <name>|--reject)`
- `--person-search <target path> <name>`
- `--person-train <target path>`
- `--person-predict <target path> [--limit <n>] [--min-confidence <0.0-1.0>]`
- `--person-review <target path> <prediction id> (--accept|--reject)`

## Scheduling

- `--schedule-import <source path> <target path> [extensions] [--delete-source] [--quality-filter off|warn|strict] [--quality-report] [--before-command "cmd"] --period daily|weekly [--time HH:mm] [--weekday mon..sun]`
- `--unschedule-import`

## Meta

- `--version` / `-v`
- `-h`
