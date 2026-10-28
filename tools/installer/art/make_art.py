"""Builds the Living Server setup's art from the launcher's own art (source/tools/OfflineDaoc.Launcher/Assets).

    python tools/installer/art/make_art.py

Writes into tools/installer/src/Assets/:
  hero.jpg     welcome page background, 1920x1200 (drawn at 960x600 logical, sharp up to 200% scaling)
  header.jpg   page banner, 1920x220, with the Living Server title baked in on the left
  shield.png   the tri-realm shield, 256 px high, transparent
  app.ico      the same shield as Setup's own exe icon (16 to 256 px)
  shortcut.ico the game shortcuts' icon: the splash painting's three figures (16 to 256 px)
  stone.jpg    a 512x512 seamless dark stone tile for page backgrounds

The welcome painting is Living Server's splash (living-server-splash-source.jpg, owner 2026-10-28); the header and
shield are the launcher's (Offline DAoC project art). The lettering is added here, in Cinzel
(SIL Open Font License, fonts/OFL-Cinzel.txt). Deterministic: the same inputs give the same files.
"""
from pathlib import Path
import random

from PIL import Image, ImageChops, ImageDraw, ImageEnhance, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parents[3]
LAUNCHER_ART = ROOT / "source/tools/OfflineDaoc.Launcher/Assets"
FONT = Path(__file__).resolve().parent / "fonts/Cinzel-wght.ttf"
OUT = ROOT / "tools/installer/src/Assets"

GOLD_TOP = (255, 236, 176)
GOLD_MID = (226, 184, 96)
GOLD_LOW = (140, 92, 34)


def cinzel(size, weight=700):
    font = ImageFont.truetype(str(FONT), size)
    font.set_variation_by_axes([weight])
    return font


def gold_text(text, font, tracking=0, stroke=2):
    """Metallic gold lettering on a transparent layer, with a dark rim and a soft drop shadow."""
    # measure with tracking
    widths = [font.getbbox(ch)[2] - font.getbbox(ch)[0] if ch != " " else font.size * 0.32 for ch in text]
    ascent, descent = font.getmetrics()
    w = int(sum(widths) + tracking * (len(text) - 1) + stroke * 2 + 24)
    h = int(ascent + descent + stroke * 2 + 24)
    mask = Image.new("L", (w, h), 0)
    rim = Image.new("L", (w, h), 0)
    d_mask, d_rim = ImageDraw.Draw(mask), ImageDraw.Draw(rim)
    x = 12 + stroke
    for ch, cw in zip(text, widths):
        if ch != " ":
            left = font.getbbox(ch)[0]
            d_rim.text((x - left, 12 + stroke), ch, font=font, fill=255, stroke_width=stroke, stroke_fill=255)
            d_mask.text((x - left, 12 + stroke), ch, font=font, fill=255)
        x += cw + tracking

    box = mask.getbbox()
    top, bottom = box[1], box[3]
    grad = Image.new("RGB", (1, h))
    for y in range(h):
        t = min(max((y - top) / max(1, bottom - top), 0), 1)
        if t < 0.45:
            k = t / 0.45
            c = [int(GOLD_TOP[i] + (GOLD_MID[i] - GOLD_TOP[i]) * k) for i in range(3)]
        elif t < 0.55:
            c = [int(GOLD_MID[i] * 0.86) for i in range(3)]  # the bright/dark break of polished metal
        else:
            k = (t - 0.55) / 0.45
            c = [int(GOLD_MID[i] + (GOLD_LOW[i] - GOLD_MID[i]) * k) for i in range(3)]
        grad.putpixel((0, y), tuple(c))
    grad = grad.resize((w, h))

    layer = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    shadow = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    shadow.putalpha(rim.filter(ImageFilter.GaussianBlur(max(3, font.size // 10))).point(lambda v: int(v * 0.9)))
    layer = Image.alpha_composite(layer, shadow)
    dark_rim = Image.new("RGBA", (w, h), (38, 24, 10, 0))
    dark_rim.putalpha(rim)
    layer = Image.alpha_composite(layer, dark_rim)
    # a faint highlight one pixel up gives the edge a bevel
    hi = Image.new("RGBA", (w, h), (255, 246, 214, 0))
    hi.putalpha(ImageChops.subtract(mask, ImageChops.offset(mask, 0, max(1, font.size // 40))).point(lambda v: int(v * 0.55)))
    face = grad.convert("RGBA")
    face.putalpha(mask)
    layer = Image.alpha_composite(layer, face)
    layer = Image.alpha_composite(layer, hi)
    return layer.crop(layer.getbbox())


def rule(width, height=3):
    """A thin gold rule that fades out at both ends, with a small diamond in the middle."""
    img = Image.new("RGBA", (width, height + 10), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    mid = width // 2
    for x in range(width):
        a = 1 - abs(x - mid) / mid
        a = int(220 * (a ** 0.7))
        d.line([(x, 5), (x, 5 + height - 2)], fill=GOLD_MID + (a,))
    d.polygon([(mid, 0), (mid + 6, 5 + height // 2), (mid, 10 + height - 1), (mid - 6, 5 + height // 2)], fill=GOLD_TOP + (255,))
    return img


def cover(img, size, focus=(0.5, 0.5)):
    """Scale to cover size, cropping around the focus point (fractions)."""
    tw, th = size
    s = max(tw / img.width, th / img.height)
    rs = img.resize((round(img.width * s), round(img.height * s)), Image.LANCZOS)
    left = int((rs.width - tw) * focus[0])
    top = int((rs.height - th) * focus[1])
    return rs.crop((left, top, left + tw, top + th))


def vignette(size, strength=0.55, bottom=0.0, right=0.0):
    """Darkening mask: edges, and optional extra darkness towards the bottom and the right."""
    w, h = size
    small = Image.new("L", (64, 40))
    for y in range(40):
        for x in range(64):
            dx, dy = (x - 31.5) / 31.5, (y - 19.5) / 19.5
            v = strength * min(1.0, (dx * dx + dy * dy) ** 0.5 * 0.75) ** 2
            v += bottom * max(0.0, (y / 39 - 0.55) / 0.45) ** 1.5
            v += right * max(0.0, (x / 63 - 0.5) / 0.5) ** 1.3
            small.putpixel((x, y), int(255 * min(1.0, v)))
    return small.resize(size, Image.BICUBIC)


def darken(img, mask, color=(10, 8, 6)):
    return Image.composite(Image.new("RGB", img.size, color), img, mask)


def make_hero():
    # Owner 2026-10-28: the welcome page uses Living Server's own splash painting (living-server-splash-source.jpg, the
    # same picture as the game's loading screen). It has no lettering, so the whole title is set here, top left, over
    # the dark storm clouds, clear of the three figures.
    src = Image.open(Path(__file__).resolve().parent / "living-server-splash-source.jpg").convert("RGB")
    W, H = 1920, 1200
    hero = cover(src, (W, H), focus=(0.5, 0.0))
    hero = ImageEnhance.Contrast(hero).enhance(1.03)
    hero = darken(hero, vignette((W, H), strength=0.42, bottom=0.6, right=0.0))

    big = gold_text("OFFLINE DAoC", cinzel(88, 800), tracking=7, stroke=3)
    sub = gold_text("CLASSIC + SHROUDED ISLES", cinzel(24, 700), tracking=5, stroke=2)
    ls = gold_text("LIVING SERVER", cinzel(66, 800), tracking=7, stroke=3)
    x, y = 72, 56
    width = max(big.width, ls.width)
    # a soft dark patch behind the title so it reads over the lightning
    band = Image.new("L", (W, H), 0)
    ImageDraw.Draw(band).ellipse((x - 200, y - 130, x + width + 120, y + big.height + sub.height + ls.height + 190), fill=185)
    band = band.filter(ImageFilter.GaussianBlur(70))
    hero = darken(hero, band, (10, 8, 6))
    hero = hero.convert("RGBA")
    hero.alpha_composite(big, (x, y))
    y += big.height + 16
    hero.alpha_composite(sub, (x + (big.width - sub.width) // 2, y))
    y += sub.height + 22
    r = rule(width)
    hero.alpha_composite(r, (x, y))
    y += r.height + 18
    hero.alpha_composite(ls, (x + (big.width - ls.width) // 2, y))
    return hero.convert("RGB")


def make_header(shield):
    src = Image.open(LAUNCHER_ART / "offline-daoc-header-source.png").convert("RGB")
    W, H = 1920, 220
    # the mountain hall, the party on the road and the gate: the middle of the panorama
    head = cover(src, (W, H), focus=(0.48, 0.52))
    head = ImageEnhance.Brightness(head).enhance(0.9)
    mask = Image.new("L", (W, H), 0)
    d = ImageDraw.Draw(mask)
    for x in range(W):  # dark on the left for the title, lighter in the middle, dark again at the far right
        t = x / W
        v = 210 if t < 0.22 else int(210 - 150 * min(1, (t - 0.22) / 0.2)) if t < 0.6 else int(60 + 120 * min(1, (t - 0.6) / 0.4))
        d.line([(x, 0), (x, H)], fill=v)
    head = darken(head, mask)
    # bottom edge: a gold hairline over a dark lip
    head = head.convert("RGBA")
    d = ImageDraw.Draw(head)
    d.rectangle((0, H - 6, W, H), fill=(14, 11, 8, 255))
    d.line([(0, H - 6), (W, H - 6)], fill=GOLD_MID + (255,), width=2)

    s = shield.resize((int(shield.width * 176 / shield.height), 176), Image.LANCZOS)
    head.alpha_composite(s, (40, 18))
    small = gold_text("OFFLINE DAoC", cinzel(34, 700), tracking=6, stroke=2)
    big = gold_text("LIVING SERVER", cinzel(74, 800), tracking=6, stroke=3)
    x = 40 + s.width + 24
    head.alpha_composite(small, (x + 4, 44))
    head.alpha_composite(big, (x, 44 + small.height + 6))
    return head.convert("RGB")


def make_shield():
    src = Image.open(LAUNCHER_ART / "offline-daoc-realm-shield-source.png").convert("RGBA")
    src = src.crop(src.getbbox())
    return src.resize((int(src.width * 256 / src.height), 256), Image.LANCZOS)


def make_icon(shield):
    sizes = [256, 128, 64, 48, 40, 32, 24, 20, 16]
    frames = []
    for n in sizes:
        canvas = Image.new("RGBA", (n, n), (0, 0, 0, 0))
        s = shield.resize((max(1, int(shield.width * n / shield.height)), n), Image.LANCZOS)
        if n <= 32:
            s = s.filter(ImageFilter.UnsharpMask(radius=1, percent=80, threshold=2))
        canvas.alpha_composite(s, ((n - s.width) // 2, 0))
        frames.append(canvas)
    return frames


def make_shortcut_icon():
    """The game shortcuts' icon (owner 2026-10-28): the three figures of the splash painting, a true square crop, in a
    rounded gold frame. Drawn at 4x and reduced, so the frame and corners stay clean at every size."""
    src = Image.open(Path(__file__).resolve().parent / "living-server-splash-source.jpg").convert("RGB")
    crop = src.crop((205, 85, 865, 745))  # 660 x 660: the dwarf, the knight and the caster
    frames = []
    for n in [256, 128, 64, 48, 40, 32, 24, 20, 16]:
        big = n * 4
        im = ImageEnhance.Contrast(crop.resize((big, big), Image.LANCZOS)).enhance(1.1)
        radius, width = big // 6, max(4, big // 36)
        mask = Image.new("L", (big, big), 0)
        ImageDraw.Draw(mask).rounded_rectangle((0, 0, big - 1, big - 1), radius=radius, fill=255)
        frame = Image.new("RGBA", (big, big), (0, 0, 0, 0))
        frame.paste(im, (0, 0), mask)
        ImageDraw.Draw(frame).rounded_rectangle((width // 2, width // 2, big - 1 - width // 2, big - 1 - width // 2),
                                                radius=radius - width // 2, outline=GOLD_MID + (255,), width=width)
        frame = frame.resize((n, n), Image.LANCZOS)
        frames.append(ImageEnhance.Sharpness(frame).enhance(1.3 if n <= 48 else 1.1))
    return frames


def make_stone():
    rnd = random.Random(1201)
    n = 512
    base = Image.new("L", (n, n), 0)
    # layered value noise, tiled (wrapping) so the texture repeats without seams
    acc = [[0.0] * n for _ in range(n)]
    for cell, amp in ((128, 0.5), (64, 0.25), (32, 0.14), (16, 0.07), (8, 0.04)):
        g = n // cell
        grid = [[rnd.random() for _ in range(g)] for _ in range(g)]
        for y in range(n):
            gy, fy = divmod(y / cell, 1)
            gy = int(gy)
            fy = fy * fy * (3 - 2 * fy)
            for x in range(n):
                gx, fx = divmod(x / cell, 1)
                gx = int(gx)
                fx = fx * fx * (3 - 2 * fx)
                a = grid[gy % g][gx % g]
                b = grid[gy % g][(gx + 1) % g]
                c = grid[(gy + 1) % g][gx % g]
                e = grid[(gy + 1) % g][(gx + 1) % g]
                acc[y][x] += amp * ((a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + e * fx) * fy)
    for y in range(n):
        for x in range(n):
            base.putpixel((x, y), int(min(255, max(0, acc[y][x] * 255))))
    lo, hi = (22, 20, 17), (50, 45, 37)
    stone = Image.merge("RGB", [base.point(lambda v, i=i: int(lo[i] + (hi[i] - lo[i]) * (v / 255) ** 1.4)) for i in range(3)])
    grain = Image.effect_noise((n, n), 18).point(lambda v: v - 128 + 128)
    stone = Image.blend(stone, Image.merge("RGB", [grain] * 3).point(lambda v: int(v * 0.25)), 0.06)
    return stone


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    shield = make_shield()
    shield.save(OUT / "shield.png", optimize=True)
    frames = make_icon(shield)
    frames[0].save(OUT / "app.ico", sizes=[(f.width, f.height) for f in frames], append_images=frames[1:])
    shortcut = make_shortcut_icon()
    shortcut[0].save(OUT / "shortcut.ico", sizes=[(f.width, f.height) for f in shortcut], append_images=shortcut[1:])
    make_hero().save(OUT / "hero.jpg", quality=90, optimize=True, progressive=True)
    make_header(shield).save(OUT / "header.jpg", quality=90, optimize=True, progressive=True)
    make_stone().save(OUT / "stone.jpg", quality=88, optimize=True)
    for f in sorted(OUT.iterdir()):
        print(f"{f.name}: {f.stat().st_size // 1024} KB")


if __name__ == "__main__":
    main()
