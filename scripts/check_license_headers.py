#!/usr/bin/env python3
# MIT License
#
# Copyright (c) 2025 Dimitri Ratz
#
# Permission is hereby granted, free of charge, to any person obtaining a copy
# of this software and associated documentation files (the "Software"), to deal
# in the Software without restriction, including without limitation the rights
# to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
# copies of the Software, and to permit persons to whom the Software is
# furnished to do so, subject to the following conditions:
#
# The above copyright notice and this permission notice shall be included in all
# copies or substantial portions of the Software.
#
# THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
# IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
# FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
# AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
# LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
# OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
# SOFTWARE.

from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parent.parent
EXTENSIONS = {".cs", ".py", ".sh"}
EXCLUDE_PARTS = {
    ".git",
    ".venv",
    "bin",
    "obj",
    "TestResults",
    "chat-session-resources",
}

REQUIRED_MARKERS = [
    "MIT License",
    "Permission is hereby granted, free of charge, to any person obtaining a copy",
    "THE SOFTWARE IS PROVIDED \"AS IS\"",
]


def main() -> int:
    candidates = []
    for path in ROOT.rglob("*"):
        if not path.is_file():
            continue
        if path.suffix.lower() not in EXTENSIONS:
            continue
        if any(part in EXCLUDE_PARTS for part in path.parts):
            continue
        candidates.append(path)

    missing = []
    for path in sorted(candidates):
        text = path.read_text(encoding="utf-8", errors="ignore")[:3500]
        if not all(marker in text for marker in REQUIRED_MARKERS):
            missing.append(path.relative_to(ROOT))

    if missing:
        print("License header check failed.")
        print(f"Checked files: {len(candidates)}")
        print(f"Missing headers: {len(missing)}")
        for path in missing:
            print(path)
        return 1

    print("License header check passed.")
    print(f"Checked files: {len(candidates)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
