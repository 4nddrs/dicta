#!/usr/bin/env bash
# Install:
#   curl -fsSL https://raw.githubusercontent.com/4nddrs/dicta/main/install.sh | bash
# Remove:
#   dicta uninstall
set -euo pipefail

# Folder that contains dicta-osx-arm64.tar.gz and dicta-linux-x64.tar.gz.
DICTA_BASE_URL="${DICTA_BASE_URL:-https://github.com/4nddrs/dicta/releases/download/v1.0.3}"

uninstall_dicta() {
  local dest="$HOME/.local/share/dicta"
  if [[ "$(uname -s)" == Darwin ]]; then
    launchctl bootout "gui/$(id -u)/com.dicta.app" >/dev/null 2>&1 || true
    rm -f "$HOME/Library/LaunchAgents/com.dicta.app.plist"
  else
    systemctl --user disable --now dicta.service >/dev/null 2>&1 || true
    rm -f "$HOME/.config/systemd/user/dicta.service"
    systemctl --user daemon-reload >/dev/null 2>&1 || true
  fi
  pkill -f "$dest/Dicta" >/dev/null 2>&1 || true
  rm -f "$HOME/.local/bin/dicta"
  rm -rf "$dest"
  echo "Dicta has been removed."
}

if [[ "${1:-}" == "uninstall" ]]; then
  uninstall_dicta
  exit 0
fi

OS="$(uname -s)"
ARCH="$(uname -m)"
case "$OS-$ARCH" in
  Darwin-arm64) RID=osx-arm64; NATIVE=macos-arm64 ;;
  Darwin-x86_64) RID=osx-x64; NATIVE=macos-x64 ;;
  Linux-x86_64) RID=linux-x64; NATIVE=linux-x64 ;;
  Linux-aarch64|Linux-arm64) RID=linux-arm64; NATIVE=linux-arm64 ;;
  *)
    echo "This installer is for 64-bit macOS or Linux."
    exit 1
    ;;
esac

ROOT=""
if [[ -n "${BASH_SOURCE[0]:-}" && -f "${BASH_SOURCE[0]}" ]]; then
  ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
fi

WORKDIR=""
cleanup() {
  if [[ -n "$WORKDIR" ]]; then
    rm -rf "$WORKDIR"
  fi
}
trap cleanup EXIT

PUB=""
if [[ -n "$ROOT" && -x "$ROOT/dist/$RID/Dicta" ]]; then
  PUB="$ROOT/dist/$RID"
elif [[ -n "$ROOT" && -f "$ROOT/src/Dicta/Dicta.csproj" ]]; then
  export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
  export PATH="$DOTNET_ROOT:$DOTNET_ROOT/tools:$PATH"
  export DOTNET_CLI_TELEMETRY_OPTOUT=1
  export DOTNET_NOLOGO=1
  if ! command -v dotnet >/dev/null 2>&1; then
    echo "The .NET 8 SDK is required to build Dicta from this folder."
    exit 1
  fi
  echo "Building Dicta for $RID..."
  PUB="$ROOT/dist/$RID"
  dotnet publish "$ROOT/src/Dicta/Dicta.csproj" -c Release -r "$RID" --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true \
    -p:DebugType=none \
    -p:DebugSymbols=false \
    -o "$PUB"
else
  if [[ -z "$DICTA_BASE_URL" ]]; then
    echo "DICTA_BASE_URL is not set."
    echo "Host install.sh and the dicta-*.tar.gz archives in one public folder, then run:"
    echo "  curl -fsSL https://HOST/install.sh | bash"
    exit 1
  fi
  WORKDIR="$(mktemp -d)"
  ARCHIVE="$WORKDIR/dicta.tar.gz"
  URL="${DICTA_BASE_URL%/}/dicta-${RID}.tar.gz"
  echo "Downloading $URL"
  curl -fL --retry 3 -o "$ARCHIVE" "$URL"
  mkdir -p "$WORKDIR/app"
  tar -xzf "$ARCHIVE" -C "$WORKDIR/app"
  PUB="$WORKDIR/app"
fi

if [[ ! -x "$PUB/Dicta" ]]; then
  echo "Dicta for $RID was not found."
  exit 1
fi

MODEL=""
for candidate in \
  "${ROOT:+$ROOT/.cache/ggml-base.en.bin}" \
  "${ROOT:+$ROOT/dist/models/ggml-base.en.bin}" \
  "$PUB/models/ggml-base.en.bin"
do
  if [[ -n "$candidate" && -f "$candidate" ]]; then
    MODEL="$candidate"
    break
  fi
done

if [[ ! -f "$PUB/models/ggml-base.en.bin" ]]; then
  mkdir -p "$PUB/models"
  if [[ -n "$MODEL" ]]; then
    cp "$MODEL" "$PUB/models/ggml-base.en.bin"
  else
    echo "Downloading the English model..."
    curl -fL --retry 3 -o "$PUB/models/ggml-base.en.bin.part" \
      "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin"
    mv "$PUB/models/ggml-base.en.bin.part" "$PUB/models/ggml-base.en.bin"
  fi
fi

if [[ ! -d "$PUB/runtimes/$NATIVE" ]]; then
  echo "Whisper libraries are missing from $PUB/runtimes/$NATIVE."
  exit 1
fi

DEST="$HOME/.local/share/dicta"
mkdir -p "$DEST" "$HOME/.local/bin"

if [[ "$OS" == Darwin ]]; then
  launchctl bootout "gui/$(id -u)/com.dicta.app" >/dev/null 2>&1 || true
else
  systemctl --user stop dicta.service >/dev/null 2>&1 || true
fi
pkill -f "$DEST/Dicta" >/dev/null 2>&1 || true
sleep 0.2

if [[ "$OS" == Darwin ]]; then
  APP="$DEST/Dicta.app"
  BIN="$APP/Contents/MacOS"
  rm -rf "$APP"
  mkdir -p "$BIN/runtimes/$NATIVE" "$DEST/models" "$APP/Contents/Resources"
  cp -f "$PUB/Dicta" "$BIN/Dicta"
  chmod +x "$BIN/Dicta"
  for lib in "$PUB"/libSkiaSharp.* "$PUB"/libHarfBuzzSharp.* "$PUB"/libAvaloniaNative.* "$PUB"/libuiohook.*; do
    [[ -f "$lib" ]] || continue
    cp -f "$lib" "$BIN/"
  done
  cp -f "$PUB/models/ggml-base.en.bin" "$DEST/models/ggml-base.en.bin"
  cp -f "$PUB/runtimes/$NATIVE/"* "$BIN/runtimes/$NATIVE/"
  cat > "$APP/Contents/Info.plist" <<'EOF'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key>
  <string>Dicta</string>
  <key>CFBundleDisplayName</key>
  <string>Dicta</string>
  <key>CFBundleIdentifier</key>
  <string>com.dicta.app</string>
  <key>CFBundleExecutable</key>
  <string>Dicta</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleShortVersionString</key>
  <string>1.0.3</string>
  <key>CFBundleVersion</key>
  <string>1.0.3</string>
  <key>LSMinimumSystemVersion</key>
  <string>12.0</string>
  <key>LSUIElement</key>
  <true/>
  <key>NSHighResolutionCapable</key>
  <true/>
  <key>NSMicrophoneUsageDescription</key>
  <string>Dicta uses the microphone to transcribe English and type it where the cursor is.</string>
</dict>
</plist>
EOF
  rm -f "$DEST/Dicta" "$DEST"/lib*.dylib
  rm -rf "$DEST/runtimes"
  xattr -cr "$APP" >/dev/null 2>&1 || true
  if command -v codesign >/dev/null 2>&1; then
    codesign --force --deep --sign - "$APP" >/dev/null 2>&1 || true
  fi
  TARGET="$BIN/Dicta"
else
  mkdir -p "$DEST/models" "$DEST/runtimes/$NATIVE"
  cp -f "$PUB/Dicta" "$DEST/Dicta"
  chmod +x "$DEST/Dicta"
  for lib in "$PUB"/libSkiaSharp.* "$PUB"/libHarfBuzzSharp.* "$PUB"/libAvaloniaNative.* "$PUB"/libuiohook.*; do
    [[ -f "$lib" ]] || continue
    cp -f "$lib" "$DEST/"
  done
  cp -f "$PUB/models/ggml-base.en.bin" "$DEST/models/ggml-base.en.bin"
  cp -f "$PUB/runtimes/$NATIVE/"* "$DEST/runtimes/$NATIVE/"
  TARGET="$DEST/Dicta"
fi

if [[ -f "$PUB/README.txt" ]]; then
  cp -f "$PUB/README.txt" "$DEST/README.txt"
elif [[ -n "$ROOT" && -f "$ROOT/src/Dicta/README.txt" ]]; then
  cp -f "$ROOT/src/Dicta/README.txt" "$DEST/README.txt"
fi
cat > "$HOME/.local/bin/dicta" <<'EOF'
#!/bin/sh
dest="$HOME/.local/share/dicta"
if [ "$(uname -s)" = Darwin ] && [ -x "$dest/Dicta.app/Contents/MacOS/Dicta" ]; then
  bin="$dest/Dicta.app/Contents/MacOS/Dicta"
else
  bin="$dest/Dicta"
fi
if [ "${1:-}" = "uninstall" ]; then
  if [ "$(uname -s)" = Darwin ]; then
    launchctl bootout "gui/$(id -u)/com.dicta.app" >/dev/null 2>&1 || true
    rm -f "$HOME/Library/LaunchAgents/com.dicta.app.plist"
  else
    systemctl --user disable --now dicta.service >/dev/null 2>&1 || true
    rm -f "$HOME/.config/systemd/user/dicta.service"
    systemctl --user daemon-reload >/dev/null 2>&1 || true
  fi
  pkill -f "$dest/Dicta" >/dev/null 2>&1 || true
  rm -f "$HOME/.local/bin/dicta"
  rm -rf "$dest"
  echo "Dicta has been removed."
  exit 0
fi
exec "$bin" "$@"
EOF
chmod +x "$HOME/.local/bin/dicta"

if [[ "$OS" == Darwin ]]; then
  mkdir -p "$HOME/Library/LaunchAgents"
  cat > "$HOME/Library/LaunchAgents/com.dicta.app.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key>
  <string>com.dicta.app</string>
  <key>ProgramArguments</key>
  <array>
    <string>$TARGET</string>
  </array>
  <key>RunAtLoad</key>
  <true/>
  <key>KeepAlive</key>
  <true/>
</dict>
</plist>
EOF
  launchctl bootstrap "gui/$(id -u)" "$HOME/Library/LaunchAgents/com.dicta.app.plist"
else
  mkdir -p "$HOME/.config/systemd/user"
  cat > "$HOME/.config/systemd/user/dicta.service" <<EOF
[Unit]
Description=Dicta

[Service]
ExecStart=$TARGET
Restart=on-failure

[Install]
WantedBy=default.target
EOF
  if command -v systemctl >/dev/null 2>&1 && [[ -n "${XDG_RUNTIME_DIR:-}" ]]; then
    systemctl --user daemon-reload
    systemctl --user enable --now dicta.service
  else
    nohup "$TARGET" >/dev/null 2>&1 &
  fi
fi

echo
echo "Dicta is installed and running."
echo "Shortcut: Ctrl + Shift + Space"
echo "Dictate from a terminal: dicta"
echo "Remove Dicta: dicta uninstall"
echo
if [[ "$OS" == Darwin ]]; then
  echo "The first time, Dicta opens a checklist."
  echo "Allow the microphone and Accessibility. Each button opens that page."
  echo
fi
if [[ "$OS" == Linux ]]; then
  if [[ -n "${WAYLAND_DISPLAY:-}" ]]; then
    echo "Wayland needs wtype and wl-clipboard."
  else
    echo "X11 needs xdotool and xclip."
  fi
  echo "The microphone uses parec (pulseaudio-utils) or arecord (alsa-utils)."
  echo
fi
if ! command -v dicta >/dev/null 2>&1; then
  echo "If the dicta command is not found, run: $HOME/.local/bin/dicta"
fi
