#!/usr/bin/env bash
# WSL から Windows 用の timer.exe をビルドして、インストール先に上書きコピーする。
#
# 必要なもの（初回だけ）:
#   rustup target add x86_64-pc-windows-msvc
#   mise use -g cargo:cargo-xwin
#   clang-cl / lld-link / llvm-rc（mise の LLVM か、sudo apt install clang lld llvm）
set -euo pipefail

cd "$(dirname "$0")"

# コピー先は Windows のパス。WSL の /mnt/c からは AppData\Local\Timer が見えないことがあるので、Windows 側でコピーする
DEST="${TIMER_DEST:-%LOCALAPPDATA%\\Timer\\timer.exe}"
TARGET=x86_64-pc-windows-msvc

if [ -f "$HOME/.cargo/env" ]; then
  . "$HOME/.cargo/env"
fi

npm ci
# インストーラは作らず、exe だけを作る。ページは exe の中に入るので、exe 1 つで動く
npm run tauri -- build --runner cargo-xwin --target "$TARGET" --no-bundle

EXE="target/$TARGET/release/timer.exe"
if ! TIMER_SRC="$(wslpath -w "$EXE")" TIMER_DEST="$DEST" WSLENV=TIMER_SRC:TIMER_DEST powershell.exe -NoProfile -Command \
  'Copy-Item -LiteralPath $env:TIMER_SRC -Destination ([Environment]::ExpandEnvironmentVariables($env:TIMER_DEST)) -Force -ErrorAction Stop'; then
  echo "error: could not overwrite $DEST (is the timer running?)" >&2
  exit 1
fi

echo "copied $EXE -> $DEST"
