#!/usr/bin/env bash
# Moodscape PC painter for Linux and macOS.  ./start.sh   (Ctrl+C to stop)
set -e
cd "$(dirname "$0")"
[ -d .venv ] || { echo "Setting up Python environment (first run only)…"; python3 -m venv .venv; }
. .venv/bin/activate
if command -v nvidia-smi >/dev/null 2>&1; then ORT=onnxruntime-gpu; echo "NVIDIA GPU found: installing onnxruntime-gpu"; else ORT=onnxruntime; fi
python -m pip install -q --disable-pip-version-check numpy pillow "$ORT" qrcode
exec python painter_server.py "$@"
