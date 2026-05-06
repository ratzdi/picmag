# Person Recognition Workflow

## Models and tuning

- Detection model default: bundled `version-RFB-320.onnx`
- Override detection model: `PICMAG_FACE_DETECTION_MODEL`
- Optional detection tuning:
  - `PICMAG_FACE_DETECTION_THRESHOLD` (default `0.30`)
  - `PICMAG_FACE_DETECTION_NMS_IOU` (default `0.45`)
  - `PICMAG_FACE_DETECTION_MAX_FACES` (default `32`)
- Optional embedding model override: `PICMAG_FACE_EMBEDDING_MODEL`

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
