# Real Person Integration Dataset (Test 16)

This directory is used by integration test 16 in `integration_test.sh`.

Provide real photos of the same person in both folders:
- `train/`: at least 5 JPG/JPEG images
- `probe/`: at least 5 JPG/JPEG images (different files from `train/`)
- `probe-negative/`: at least 5 JPG/JPEG images of other persons (negative set)

Notes:
- The test uses exactly 5 images from each folder (sorted by filename).
- Files in `train/`, `probe/`, and `probe-negative/` are ignored by git (except `.gitkeep`).
- You can override the dataset path with:
  `PICMAG_PERSON_ITEST_DATASET=/absolute/path/to/dataset bash ./integration_test.sh`
  where that dataset contains `train/`, `probe/`, and `probe-negative/` subfolders.
