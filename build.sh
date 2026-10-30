#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$PATH"

python3 "$ROOT/tools/make_icon.py"

dotnet publish "$ROOT/src/Dicta/Dicta.csproj" \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -p:DebugType=none \
  -p:DebugSymbols=false \
  -o "$ROOT/dist"

rm -rf \
  "$ROOT/dist/runtimes/linux-arm" \
  "$ROOT/dist/runtimes/linux-arm64" \
  "$ROOT/dist/runtimes/linux-x64" \
  "$ROOT/dist/runtimes/macos-arm64" \
  "$ROOT/dist/runtimes/macos-x64" \
  "$ROOT/dist/ggml-metal.metal"

MODEL="$ROOT/dist/models/ggml-base.en.bin"
mkdir -p "$ROOT/dist/models"
if [[ -f "$ROOT/.cache/ggml-base.en.bin" ]]; then
  cp "$ROOT/.cache/ggml-base.en.bin" "$MODEL"
elif [[ ! -f "$MODEL" ]]; then
  echo "Downloading the English model…"
  curl -L --fail --retry 3 -A "Dicta/1.0" \
    -o "$ROOT/.cache/ggml-base.en.bin.part" \
    "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin"
  mv "$ROOT/.cache/ggml-base.en.bin.part" "$ROOT/.cache/ggml-base.en.bin"
  cp "$ROOT/.cache/ggml-base.en.bin" "$MODEL"
fi

echo "Ready: $ROOT/dist/Dicta.exe"
