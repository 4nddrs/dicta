#!/usr/bin/env bash
# Build the archives that install.sh downloads.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/packaging/release"
mkdir -p "$OUT"

model=""
for candidate in \
  "$ROOT/dist/models/ggml-base.en.bin" \
  "$ROOT/.cache/ggml-base.en.bin"
do
  if [[ -f "$candidate" ]]; then
    model="$candidate"
    break
  fi
done
if [[ -z "$model" ]]; then
  echo "The English model is missing. Expected dist/models/ggml-base.en.bin."
  exit 1
fi

pack() {
  local rid="$1"
  local native="$2"
  local stage="$ROOT/dist/$rid"
  local tmp
  tmp="$(mktemp -d)"
  if [[ ! -x "$stage/Dicta" ]]; then
    echo "Skipping $rid. $stage/Dicta is missing."
    rm -rf "$tmp"
    return
  fi
  if [[ ! -d "$stage/runtimes/$native" ]]; then
    echo "Skipping $rid. Whisper libraries are missing."
    rm -rf "$tmp"
    return
  fi
  cp -f "$stage/Dicta" "$tmp/Dicta"
  chmod +x "$tmp/Dicta"
  local lib
  for lib in "$stage"/libSkiaSharp.* "$stage"/libHarfBuzzSharp.* "$stage"/libAvaloniaNative.* "$stage"/libuiohook.*; do
    [[ -f "$lib" ]] || continue
    cp -f "$lib" "$tmp/"
  done
  if [[ -f "$ROOT/src/Dicta/README.txt" ]]; then
    cp -f "$ROOT/src/Dicta/README.txt" "$tmp/README.txt"
  fi
  mkdir -p "$tmp/models" "$tmp/runtimes/$native"
  if [[ -f "$stage/models/ggml-base.en.bin" ]]; then
    cp -f "$stage/models/ggml-base.en.bin" "$tmp/models/ggml-base.en.bin"
  else
    cp -f "$model" "$tmp/models/ggml-base.en.bin"
  fi
  cp -f "$stage/runtimes/$native/"* "$tmp/runtimes/$native/"
  tar -czf "$OUT/dicta-${rid}.tar.gz" -C "$tmp" .
  rm -rf "$tmp"
  echo "Wrote $OUT/dicta-${rid}.tar.gz"
}

pack osx-arm64 macos-arm64
pack osx-x64 macos-x64
pack linux-x64 linux-x64
pack linux-arm64 linux-arm64
