# Versioning policy (Semantic Versioning)

This project follows [Semantic Versioning](https://semver.org/) using:

`MAJOR.MINOR.PATCH`

## Meaning in this project

- `MAJOR`:
  - Increase when introducing breaking changes.
  - Typical examples: incompatible CLI argument changes, changed import behavior that breaks existing automation, incompatible DB schema/format changes.
- `MINOR`:
  - Increase when adding backward-compatible functionality.
  - Typical examples: new import options, new file format support, new non-breaking commands.
- `PATCH`:
  - Increase for backward-compatible bug fixes only.
  - Typical examples: fix import edge cases, stability/logging fixes, test-only fixes without feature changes.

## Pre-1.0 policy (`0.y.z`)

Until `1.0.0`, the project uses this stricter rule for clarity:

- `0.y.z` `MINOR` (`y`) is used for any user-visible feature change.
- `PATCH` (`z`) is used for bug fixes and internal improvements.

Current recommended baseline for this branch's feature set is `0.4.1`.

## Release process

1. Ensure `master` is green (build + tests).
2. Update changelog/release notes with sections:
   - Added
   - Changed
   - Fixed
3. Set the project version in [picmag/picmag.csproj](picmag/picmag.csproj).
4. Create an annotated tag:

```bash
git tag -a vX.Y.Z -m "Release vX.Y.Z"
git push origin vX.Y.Z
```

## Commit message guidance

Use Conventional Commit prefixes to simplify release decisions:

- `feat:` -> usually `MINOR`
- `fix:` -> usually `PATCH`
- `refactor:`, `chore:`, `test:`, `docs:` -> no automatic bump unless user-visible behavior changes

## Examples

- `0.1.0` -> adds `--delete-source` feature.
- `0.1.1` -> fixes source-deletion error handling/logging without changing CLI contract.
- `0.2.0` -> adds mp4 import support.
- `1.0.0` -> first stable contract for CLI + import behavior.
