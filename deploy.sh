#!/usr/bin/env bash
# WSL から Windows 用の timer.exe をビルドして、インストール先に上書きコピーする。
#
# 必要なもの（初回だけ）:
#   rustup target add x86_64-pc-windows-msvc
#   mise use -g cargo:cargo-xwin
#   clang-cl / lld-link / llvm-rc（mise の LLVM か、sudo apt install clang lld llvm）
set -euo pipefail

cd "$(dirname "$0")"

# Windows の環境変数は cmd.exe から読む。コピーは WSL の cp で行う
# （パッケージアプリの中から動かす powershell.exe だと、AppData への書き込みがアプリ専用の場所に振り替えられる）
LOCALAPPDATA_WIN="$(cd /mnt/c && cmd.exe /c 'echo %LOCALAPPDATA%' | tr -d '\r')"
DEST="${TIMER_DEST:-$(wslpath -u "$LOCALAPPDATA_WIN")/Timer/timer.exe}"
TARGET=x86_64-pc-windows-msvc

if [ -f "$HOME/.cargo/env" ]; then
  . "$HOME/.cargo/env"
fi

npm ci
# インストーラは作らず、exe だけを作る。ページは exe の中に入るので、exe 1 つで動く
npm run tauri -- build --runner cargo-xwin --target "$TARGET" --no-bundle

EXE="target/$TARGET/release/timer.exe"
if ! cp "$EXE" "$DEST"; then
  echo "error: could not overwrite $DEST (is the timer running, or not installed yet?)" >&2
  exit 1
fi

echo "copied $EXE -> $DEST"
