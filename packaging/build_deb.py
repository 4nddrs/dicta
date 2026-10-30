#!/usr/bin/env python3
"""Build packaging/dicta_1.0.0_amd64.deb from dist/linux-x64."""

import io
import os
import stat
import tarfile
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
STAGE = ROOT / "dist" / "linux-x64"
NATIVE = STAGE / "runtimes" / "linux-x64"
OUT = Path(__file__).resolve().parent / "dicta_1.0.0_amd64.deb"
VERSION = "1.0.0"


def model_path() -> Path:
    candidates = [
        STAGE / "models" / "ggml-base.en.bin",
        ROOT / "dist" / "models" / "ggml-base.en.bin",
        ROOT / ".cache" / "ggml-base.en.bin",
    ]
    for path in candidates:
        if path.is_file() and path.stat().st_size > 100_000_000:
            return path
    raise SystemExit("The English model is missing. Expected dist/models/ggml-base.en.bin.")


def add_file(tar: tarfile.TarFile, arcname: str, source: Path, mode: int) -> None:
    info = tarfile.TarInfo(arcname)
    info.size = source.stat().st_size
    info.mode = mode
    info.mtime = int(source.stat().st_mtime)
    info.uid = 0
    info.gid = 0
    info.uname = "root"
    info.gname = "root"
    with source.open("rb") as handle:
        tar.addfile(info, handle)


def add_bytes(tar: tarfile.TarFile, arcname: str, data: bytes, mode: int) -> None:
    info = tarfile.TarInfo(arcname)
    info.size = len(data)
    info.mode = mode
    info.mtime = int(time.time())
    info.uid = 0
    info.gid = 0
    info.uname = "root"
    info.gname = "root"
    tar.addfile(info, io.BytesIO(data))


def add_dir(tar: tarfile.TarFile, arcname: str) -> None:
    info = tarfile.TarInfo(arcname)
    info.type = tarfile.DIRTYPE
    info.mode = 0o755
    info.mtime = int(time.time())
    info.uid = 0
    info.gid = 0
    info.uname = "root"
    info.gname = "root"
    tar.addfile(info)


def build_data() -> tuple[bytes, int]:
    if not (STAGE / "Dicta").is_file():
        raise SystemExit("dist/linux-x64/Dicta is missing.")
    if not NATIVE.is_dir() or not any(NATIVE.iterdir()):
        raise SystemExit("dist/linux-x64/runtimes/linux-x64 is missing.")

    size = 0
    buffer = io.BytesIO()
    with tarfile.open(fileobj=buffer, mode="w:gz", format=tarfile.GNU_FORMAT) as tar:
        for directory in (
            "./usr",
            "./usr/bin",
            "./usr/lib",
            "./usr/lib/dicta",
            "./usr/lib/dicta/models",
            "./usr/lib/dicta/runtimes",
            "./usr/lib/dicta/runtimes/linux-x64",
            "./usr/lib/systemd",
            "./usr/lib/systemd/user",
            "./usr/share",
            "./usr/share/doc",
            "./usr/share/doc/dicta",
        ):
            add_dir(tar, directory)

        wrapper = b"#!/bin/sh\nexec /usr/lib/dicta/Dicta \"$@\"\n"
        add_bytes(tar, "./usr/bin/dicta", wrapper, 0o755)
        size += len(wrapper)
        binary = STAGE / "Dicta"
        add_file(tar, "./usr/lib/dicta/Dicta", binary, 0o755)
        size += binary.stat().st_size
        for path in sorted(STAGE.glob("lib*")):
            if path.is_file():
                add_file(tar, f"./usr/lib/dicta/{path.name}", path, 0o755)
                size += path.stat().st_size
        for path in sorted(NATIVE.iterdir()):
            if path.is_file():
                add_file(tar, f"./usr/lib/dicta/runtimes/linux-x64/{path.name}", path, 0o755)
                size += path.stat().st_size
        weights = model_path()
        add_file(tar, "./usr/lib/dicta/models/ggml-base.en.bin", weights, 0o644)
        size += weights.stat().st_size
        service = Path(__file__).resolve().parent / "dicta.service"
        add_file(tar, "./usr/lib/systemd/user/dicta.service", service, 0o644)
        readme = ROOT / "src" / "Dicta" / "README.txt"
        if readme.is_file():
            add_file(tar, "./usr/share/doc/dicta/README.txt", readme, 0o644)
    return buffer.getvalue(), max(1, size // 1024)


def build_control(installed_size: int) -> bytes:
    text = (
        "Package: dicta\n"
        f"Version: {VERSION}\n"
        "Section: utils\n"
        "Priority: optional\n"
        "Architecture: amd64\n"
        "Maintainer: Dicta <dicta@local>\n"
        f"Installed-Size: {installed_size}\n"
        "Recommends: wtype, wl-clipboard, pulseaudio-utils, xdotool, xclip\n"
        "Description: English dictation at the cursor\n"
        " Dicta listens from the microphone and types English where the cursor is.\n"
    ).encode()
    scripts = Path(__file__).resolve().parent / "debian"
    buffer = io.BytesIO()
    with tarfile.open(fileobj=buffer, mode="w:gz", format=tarfile.GNU_FORMAT) as tar:
        add_bytes(tar, "./control", text, 0o644)
        for name in ("postinst", "prerm", "postrm"):
            path = scripts / name
            data = path.read_bytes()
            if not data.endswith(b"\n"):
                data += b"\n"
            add_bytes(tar, f"./{name}", data, 0o755)
    return buffer.getvalue()


def ar_member(name: str, data: bytes) -> bytes:
    header = f"{name:<16}{int(time.time()):<12}{0:<6}{0:<6}{0o100644:<8}{len(data):<10}`\n"
    padding = b"\n" if len(data) % 2 else b""
    return header.encode("ascii") + data + padding


def main() -> None:
    data, installed_size = build_data()
    control = build_control(installed_size)
    package = b"!<arch>\n"
    package += ar_member("debian-binary", b"2.0\n")
    package += ar_member("control.tar.gz", control)
    package += ar_member("data.tar.gz", data)
    OUT.write_bytes(package)
    os.chmod(OUT, stat.S_IRUSR | stat.S_IWUSR | stat.S_IRGRP | stat.S_IROTH)
    print(f"Wrote {OUT} ({OUT.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
