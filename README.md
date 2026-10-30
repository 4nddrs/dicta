# Dicta

Dicta dictates English and types it where the cursor is.

The voice model runs on your computer. After Dicta is installed, it does not need the internet. Press the shortcut, speak English, pause, and the text is pasted into the field that had the cursor.

## Install

### macOS and Linux

macOS and Linux already include `curl`. Homebrew is not required.

```bash
curl -fsSL https://raw.githubusercontent.com/4nddrs/dicta/main/install.sh | bash
```

This downloads the build for that computer, installs it under `~/.local`, and starts it.

- macOS: Apple silicon
- Linux: 64-bit x86

The first time on macOS, Dicta opens a checklist. Allow the microphone and Accessibility. Each button opens that page, and Dicta appears in the list after you accept the prompt.

On Linux, Wayland needs `wtype` and `wl-clipboard`. X11 needs `xdotool` and `xclip`. The microphone uses `parec` (`pulseaudio-utils`) or `arecord` (`alsa-utils`).

If the `dicta` command is not found, open a new terminal or run `~/.local/bin/dicta`.

### Windows

Copy the whole `dist` folder to the PC, not only `Dicta.exe`. The model and libraries sit beside the exe. Windows 10 or 11, 64-bit, is required. Then open `Dicta.exe` and leave it in the tray.

Allow microphone access for desktop apps under Settings, Privacy & security, Microphone.

## Use

1. Click the text field you want to write in.
2. Press Ctrl + Shift + Space.
3. Speak English.
4. Pause for about 2 seconds, or press the shortcut again.

The text is pasted into that window, and the listening card closes. Ctrl+Z (Command+Z on macOS) usually undoes the whole dictation.

Running `dicta` again starts or stops a dictation. The tray menu can also start dictation, show this help, or quit.

## Uninstall

### macOS and Linux

```bash
dicta uninstall
```

That stops Dicta and deletes the app, the model, settings, and the log.

If Dicta was installed before this command existed, use:

```bash
curl -fsSL https://raw.githubusercontent.com/4nddrs/dicta/main/install.sh | bash -s uninstall
```

### Windows

Quit Dicta from the tray icon, then delete the folder that contains `Dicta.exe`.

## Files

On macOS and Linux the app, model, settings, and log are in `~/.local/share/dicta`. On macOS the program itself is `Dicta.app` inside that folder. The `dicta` command is `~/.local/bin/dicta`.

On Windows these files sit next to `Dicta.exe`:

- `models\ggml-base.en.bin`
- `runtimes\win-x64\`
- `dicta-settings.json`
- `dicta.log`
