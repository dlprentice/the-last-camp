#!/usr/bin/env bash
# Kept as a convenient entry point; the Python exporter retains all logs.
set -euo pipefail
cd "$(dirname "$0")/.."
if [[ $# -gt 0 ]]; then
  exec python3 tools/export_linux.py --output-dir "$1"
else
  exec python3 tools/export_linux.py
fi
