#!/usr/bin/env python3
"""Dicta icon: an indigo squircle with a white microphone."""

import struct
import zlib
from pathlib import Path

SIZE = 256


def blend(buf, x, y, r, g, b, a):
    if not (0 <= x < SIZE and 0 <= y < SIZE) or a <= 0:
        return
    i = (y * SIZE + x) * 4
    src_a = a / 255
    dst_a = buf[i + 3] / 255
    out_a = src_a + dst_a * (1 - src_a)
    if out_a <= 0:
        return
    for channel, value in enumerate((r, g, b)):
        dst = buf[i + channel] / 255
        src = value / 255
        buf[i + channel] = int(round(255 * (src * src_a + dst * dst_a * (1 - src_a)) / out_a))
    buf[i + 3] = int(round(255 * out_a))


def cover_round_rect(px, py, x, y, w, h, radius):
    samples = ((0.25, 0.25), (0.75, 0.25), (0.25, 0.75), (0.75, 0.75))
    hit = 0
    for ox, oy in samples:
        sx = px + ox
        sy = py + oy
        cx = min(max(sx, x + radius), x + w - radius)
        cy = min(max(sy, y + radius), y + h - radius)
        if (sx - cx) ** 2 + (sy - cy) ** 2 <= radius ** 2 or (
            x <= sx <= x + w and y <= sy <= y + h and (
                x + radius <= sx <= x + w - radius or y + radius <= sy <= y + h - radius
            )
        ):
            hit += 1
    return hit / 4


def fill_round_rect(buf, x, y, w, h, radius, color):
    for py in range(int(y) - 1, int(y + h) + 2):
        for px in range(int(x) - 1, int(x + w) + 2):
            coverage = cover_round_rect(px, py, x, y, w, h, radius)
            if coverage:
                blend(buf, px, py, *color[:3], int(color[3] * coverage))


def stroke_arc(buf, cx, cy, radius, width, y_min, color):
    half = width / 2
    for py in range(int(cy - radius - half - 2), int(cy + radius + half + 2)):
        for px in range(int(cx - radius - half - 2), int(cx + radius + half + 2)):
            if py + 0.5 < y_min:
                continue
            dist = ((px + 0.5 - cx) ** 2 + (py + 0.5 - cy) ** 2) ** 0.5
            delta = abs(dist - radius)
            if delta <= half:
                coverage = min(1, half - delta + 0.6)
                blend(buf, px, py, *color[:3], int(color[3] * coverage))


def png(width, height, rgba):
    raw = b"".join(b"\x00" + rgba[y * width * 4:(y + 1) * width * 4] for y in range(height))

    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

    ihdr = struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", ihdr) + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")


def scale(src, size, dst_size):
    out = bytearray(dst_size * dst_size * 4)
    for y in range(dst_size):
        for x in range(dst_size):
            x0 = int(x * size / dst_size)
            x1 = max(x0 + 1, int((x + 1) * size / dst_size))
            y0 = int(y * size / dst_size)
            y1 = max(y0 + 1, int((y + 1) * size / dst_size))
            acc = [0, 0, 0, 0]
            count = 0
            for yy in range(y0, min(y1, size)):
                for xx in range(x0, min(x1, size)):
                    i = (yy * size + xx) * 4
                    for c in range(4):
                        acc[c] += src[i + c]
                    count += 1
            o = (y * dst_size + x) * 4
            for c in range(4):
                out[o + c] = int(round(acc[c] / count))
    return out


def ico(images):
    header = struct.pack("<HHH", 0, 1, len(images))
    entries = b""
    payload = b""
    offset = 6 + 16 * len(images)
    for size, data in images:
        entry_size = size if size < 256 else 0
        entries += struct.pack("<BBBBHHII", entry_size, entry_size, 0, 0, 1, 32, len(data), offset)
        payload += data
        offset += len(data)
    return header + entries + payload


def main():
    buf = bytearray(SIZE * SIZE * 4)
    white = (255, 255, 255, 255)
    fill_round_rect(buf, 16, 16, 224, 224, 56, (110, 98, 246, 255))
    fill_round_rect(buf, 104, 68, 48, 78, 24, white)
    fill_round_rect(buf, 121, 140, 14, 34, 6, white)
    stroke_arc(buf, 128, 146, 42, 8, 150, white)
    fill_round_rect(buf, 110, 186, 36, 10, 5, white)

    root = Path(__file__).resolve().parents[1] / "src" / "Dicta" / "Assets"
    root.mkdir(parents=True, exist_ok=True)
    (root / "icon.png").write_bytes(png(SIZE, SIZE, buf))
    images = [(size, png(size, size, scale(buf, SIZE, size))) for size in (16, 32, 48, 256)]
    (root / "icon.ico").write_bytes(ico(images))


if __name__ == "__main__":
    main()
