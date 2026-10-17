#!/usr/bin/env bash
# Build a self-contained single-file Timer.exe and copy it to Dropbox.
set -euo pipefail

cd "$(dirname "$0")"

DEST="${TIMER_DEST:-/mnt/c/Users/rot1024/Dropbox/creation/misc/timer/TimerApp.exe}"

export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

dotnet publish TimerApp/Timer.csproj \
  -c Release \
  -r win-x64 \
  --self-contained \
  -p:PublishSingleFile=true \
  -o dist

# WinForms does not need the WPF native DLLs that are published next to the exe,
# so only Timer.exe is copied.
if ! cp dist/Timer.exe "$DEST"; then
  echo "error: could not overwrite $DEST (is the timer running?)" >&2
  exit 1
fi

echo "copied dist/Timer.exe -> $DEST"
