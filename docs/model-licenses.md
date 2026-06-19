# Model License Status

This repository contains ONNX model files that are third-party assets.
They are not automatically covered by the project MIT license.

## Bundled model files

- version-RFB-320.onnx
- retinanet-9.onnx
- arcfaceresnet100-8.onnx

## Local checksums (sha256)

- version-RFB-320.onnx: 34cd7e60aeff28744c657de7a3dc64e872d506741de66987f3426f2b79f88017
- retinanet-9.onnx: 06742923960ec4d9899e6fe407d4d2df013fe6962504f099463ca1b8cba45e44
- arcfaceresnet100-8.onnx: f3a6bc281e72f88862f5748b53be3d76b3b48f8f1ab1f4a537941bdc4e1b01da

## Source and license mapping

### version-RFB-320.onnx

- Intended upstream: Linzaer Ultra-Light-Fast-Generic-Face-Detector-1MB
- Evidence:
  - Upstream model list includes version-RFB-320.onnx in models/onnx
  - Repository license is MIT
- Sources:
  - https://github.com/Linzaer/Ultra-Light-Fast-Generic-Face-Detector-1MB
  - https://raw.githubusercontent.com/Linzaer/Ultra-Light-Fast-Generic-Face-Detector-1MB/master/models/readme
  - https://raw.githubusercontent.com/Linzaer/Ultra-Light-Fast-Generic-Face-Detector-1MB/master/LICENSE
- License used in this project metadata: MIT

### arcfaceresnet100-8.onnx

- Upstream: ONNX Model Zoo ArcFace model
- Evidence:
  - ArcFace README references model/arcfaceresnet100-8.onnx
  - ArcFace README license section states Apache 2.0
- Sources:
  - https://github.com/onnx/models/tree/main/validated/vision/body_analysis/arcface
  - https://raw.githubusercontent.com/onnx/models/main/validated/vision/body_analysis/arcface/README.md
- License used in this project metadata: Apache-2.0

### retinanet-9.onnx

- Upstream: ONNX Model Zoo RetinaNet model
- Evidence:
  - RetinaNet README references model/retinanet-9.onnx
  - RetinaNet README license section states BSD 3-Clause
- Sources:
  - https://github.com/onnx/models/tree/main/validated/vision/object_detection_segmentation/retinanet
  - https://raw.githubusercontent.com/onnx/models/main/validated/vision/object_detection_segmentation/retinanet/README.md
- License used in this project metadata: BSD-3-Clause

## Verification notes

- Mapping is based on upstream filename/path matches plus model architecture naming observed in model tensors.
- If strict supply-chain verification is required, additionally archive exact upstream blobs and compare checksums against local files.

## Redistribution notes

- Keep this file and Debian copyright metadata in sync.
- Preserve all required attribution and license texts for third-party model files.
