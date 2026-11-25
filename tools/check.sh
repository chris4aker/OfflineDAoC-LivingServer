#!/usr/bin/env bash
# Every Python check CI runs, before you push (issue #615; AGENTS.md). The real work is tools/check.py, which also runs
# on Windows: python tools\check.py
set -euo pipefail
cd "$(dirname "$0")/.."
exec "${PYTHON:-python3}" tools/check.py "$@"
