# Person Recognition Workflow

This document describes the full person recognition lifecycle in picmag, from face detection to prediction review.

The workflow is split into two phases:

1. Build trustworthy labeled data (scan, label, reject false positives)
2. Train profiles and review automated predictions

## Models and tuning

- Detection model default: bundled `version-RFB-320.onnx`
- Override detection model: `PICMAG_FACE_DETECTION_MODEL`
- Optional detection tuning:
  - `PICMAG_FACE_DETECTION_THRESHOLD` (default `0.30`)
  - `PICMAG_FACE_DETECTION_NMS_IOU` (default `0.45`)
  - `PICMAG_FACE_DETECTION_MAX_FACES` (default `32`)
- Optional embedding model override: `PICMAG_FACE_EMBEDDING_MODEL`

### What these settings do

- `PICMAG_FACE_DETECTION_MODEL`: path to the ONNX face detector used during `--person-scan-existing`.
- `PICMAG_FACE_DETECTION_THRESHOLD`: minimum detector confidence. Higher values reduce false positives but can miss small/hard faces.
- `PICMAG_FACE_DETECTION_NMS_IOU`: overlap threshold used by non-maximum suppression. Lower values are stricter at removing overlapping boxes.
- `PICMAG_FACE_DETECTION_MAX_FACES`: cap of faces per image processed in one pass.
- `PICMAG_FACE_EMBEDDING_MODEL`: path to ONNX model that creates face embeddings used for training/prediction.

## End-to-end workflow

### 1) Scan existing media for faces

```bash
picmag --person-scan-existing <target path> --only-missing
```

- Default mode is `--only-missing`.
- Use `--all` to force a full re-scan.
- Recommended first run after import or when detector settings changed.

### 2) Build labeled data set

List open labeling candidates:

```bash
picmag --person-label <target path> --limit 100
```

- If `--face-id` is not provided, the command lists unlabeled faces.
- Default `--limit` is `50`.

Create a person identity:

```bash
picmag --person-add <target path> "Max Mustermann"
```

Assign labels:

```bash
picmag --person-label <target path> --face-id 101 --person "Max Mustermann"
```

Reject false detections or unusable crops:

```bash
picmag --person-label <target path> --face-id 115 --reject
```

Validate by searching confirmed labels:

```bash
picmag --person-search <target path> "Max Mustermann"
```

### 3) Train person profiles

```bash
picmag --person-train <target path>
```

- Run training after significant new labels are added.
- Training uses confirmed labels to build per-person embedding profiles.

### 4) Generate predictions for remaining unlabeled faces

```bash
picmag --person-predict <target path> --limit 100 --min-confidence 0.75
```

- Default `--limit` is `100`.
- Default `--min-confidence` is `0.75`.
- Valid confidence range is `0.0` to `1.0`.
- Lower confidence values increase recall but typically require more manual review.

### 5) Review predictions

Accept a suggestion:

```bash
picmag --person-review <target path> <prediction id> --accept
```

Reject a suggestion:

```bash
picmag --person-review <target path> <prediction id> --reject
```

- Review action is mandatory (`--accept` or `--reject`).
- After many accepted predictions, run `--person-train` again to refresh profiles.

## Typical labeling flow

```bash
# 1) Detect faces for imported images
picmag --person-scan-existing <target path> --only-missing

# 2) List unlabeled faces
picmag --person-label <target path> --limit 100

# 3) Create/reuse person
picmag --person-add <target path> "Max Mustermann"

# 4) Assign face IDs
picmag --person-label <target path> --face-id 101 --person "Max Mustermann"

# 5) Reject false detections
picmag --person-label <target path> --face-id 115 --reject

# 6) Verify by person name
picmag --person-search <target path> "Max Mustermann"
```

## Training and prediction

```bash
# Build profiles from confirmed labels
picmag --person-train <target path>

# Generate suggestions
picmag --person-predict <target path> --limit 100 --min-confidence 0.75

# Review one prediction
picmag --person-review <target path> <prediction id> --accept
```

## Command behavior and constraints

### `--person-label`

- List mode: `picmag --person-label <target path> [--limit N]`
- Label mode: `picmag --person-label <target path> --face-id <id> (--person <name>|--reject)`
- `--person` and `--reject` are mutually exclusive.
- A `--face-id` is required when setting `--person` or `--reject`.

### `--person-predict`

- Usage: `picmag --person-predict <target path> [--limit <n>] [--min-confidence <0.0-1.0>]`
- `--limit` must be greater than `0`.
- `--min-confidence` must be between `0` and `1`.

### `--person-review`

- Usage: `picmag --person-review <target path> <prediction id> (--accept|--reject)`
- Requires exactly one prediction id and one action.

## Operational recommendations

- Start with high precision labels: prefer rejecting uncertain detections over guessing names.
- Train periodically, not continuously: batch label changes and retrain to keep feedback loops clear.
- Keep `--min-confidence` conservative (for example `0.75`) until your labeled set is large and clean.
- Re-run `--person-scan-existing --all` after model switch or major detector tuning changes.

## Troubleshooting

- No suggestions from `--person-predict`:
  - Ensure you have enough confirmed labels per person.
  - Check if `--min-confidence` is set too high.
  - Re-run `--person-train` after adding labels.

- Too many wrong predictions:
  - Increase `--min-confidence`.
  - Reject low-quality face crops during labeling.
  - Review detector threshold (`PICMAG_FACE_DETECTION_THRESHOLD`) to reduce noisy detections.

- Face not detected though visible:
  - Decrease detector threshold slightly.
  - Re-scan with `--person-scan-existing <target path> --all`.
  - Verify detector model path if `PICMAG_FACE_DETECTION_MODEL` is set.
