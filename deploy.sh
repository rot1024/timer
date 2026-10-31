#!/usr/bin/env bash
# WSL / Linux から Windows 用の Timer.exe をビルドして、Dropbox にコピーする。
#
# 必要なもの（初回だけ）:
#   rustup target add x86_64-pc-windows-msvc
#   cargo install --locked cargo-xwin
#   sudo apt install clang lld llvm   # cargo-xwin が clang-cl / lld-link / llvm-rc を使う
set -euo pipefail

cd "$(dirname "$0")"

DEST="${TIMER_DEST:-/mnt/c/Users/rot1024/Dropbox/creation/misc/timer/TimerApp.exe}"
TARGET=x86_64-pc-windows-msvc

npm ci
# インストーラは作らず、exe だけを作る。ページは exe の中に入るので、exe 1 つで動く
npm run tauri -- build --runner cargo-xwin --target "$TARGET" --no-bundle

EXE="target/$TARGET/release/timer.exe"
if ! cp "$EXE" "$DEST"; then
  echo "error: could not overwrite $DEST (is the timer running?)" >&2
  exit 1
fi

echo "copied $EXE -> $DEST"
