Dicta
=====

Portable app that dictates English and types it where the cursor is.

How to use
----------
1. Open Dicta.exe. The English model is already in the models folder.
2. Leave the app in the tray, next to the clock.
3. Click the text field you want to write in.
4. Press Ctrl + Shift + Space.
5. Speak English.
6. Pause for about 2 seconds, or press the shortcut again.

The text is pasted into that window. Ctrl+Z usually undoes the whole dictation.

Better accuracy
---------------
The model in the models folder is small. For more accurate text, add an OpenAI key:
open the tray menu, choose "OpenAI key...", paste the key, and click Save.

Dicta then sends each recording to OpenAI and needs the internet while dictating.
It uses the best model the key can reach: gpt-transcribe when the OpenAI project
has it, otherwise gpt-4o-transcribe or gpt-4o-mini-transcribe.
The key is stored in dicta-settings.json next to this file. Clear the field and
save to go back to the model on this computer.

Requirements
------------
- Windows 10 or 11, 64-bit, or macOS, or Linux
- A microphone
- A CPU with AVX2 (typical on computers from the last several years)

On Windows, copy this whole folder, not just the exe. The model and libraries sit beside it.
If the folder also holds other platforms, the files Windows needs are Dicta.exe, models, runtimes, and this file.

macOS and Linux
---------------
Other people do not need Homebrew. They run:

    curl -fsSL https://raw.githubusercontent.com/4nddrs/dicta/main/install.sh | bash

That downloads the build for their computer, installs it under ~/.local, and starts it.
The shortcut is still Ctrl + Shift + Space. Running `dicta` again starts or stops a dictation.

From this project folder, `bash install.sh` installs the local build instead of downloading one.

On macOS, the first launch opens a checklist. Allow the microphone and Accessibility there.
On Linux, Wayland needs wtype and wl-clipboard. X11 needs xdotool and xclip.
Settings and the log are in ~/.local/share/dicta.

Uninstall
---------
On macOS and Linux, run: dicta uninstall
On Windows, quit Dicta from the tray, then delete its folder.

Microphone permission
---------------------
Settings > Privacy & security > Microphone
Turn on access for desktop apps.

Files
-----
Dicta.exe
models\ggml-base.en.bin
runtimes\win-x64\
dicta-settings.json
dicta.log
