#!/usr/bin/env bash
# Moodscape Mirror for Linux and macOS.  ./mirror.sh   (Ctrl+C to stop)
exec "$(dirname "$0")/start.sh" --qr mirror "$@"
