#!/usr/bin/env python3
"""Renders the launcher icon's PNG sizes from hicolor/scalable/apps/offline-daoc-living-server.svg.

Run it after changing the SVG:  python3 linux/icons/render_icons.py
Needs librsvg's GObject bindings (Ubuntu: gir1.2-rsvg-2.0, python3-gi, python3-gi-cairo).
"""
import pathlib

import cairo
import gi

gi.require_version("Rsvg", "2.0")
from gi.repository import Rsvg  # noqa: E402

ID = "offline-daoc-living-server"
SIZES = (16, 24, 32, 48, 64, 128, 256)
ROOT = pathlib.Path(__file__).resolve().parent / "hicolor"


def main():
    handle = Rsvg.Handle.new_from_file(str(ROOT / "scalable" / "apps" / f"{ID}.svg"))
    for size in SIZES:
        surface = cairo.ImageSurface(cairo.FORMAT_ARGB32, size, size)
        viewport = Rsvg.Rectangle()
        viewport.x, viewport.y, viewport.width, viewport.height = 0, 0, size, size
        handle.render_document(cairo.Context(surface), viewport)
        out = ROOT / f"{size}x{size}" / "apps" / f"{ID}.png"
        out.parent.mkdir(parents=True, exist_ok=True)
        surface.write_to_png(str(out))
        print(out.relative_to(ROOT.parent.parent))


if __name__ == "__main__":
    main()
