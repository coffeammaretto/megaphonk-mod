"""Draws the run modifier icons of the co-op leaderboard: src/plugin/Assets/Modifiers/<Name>.png (64x64) and the raw
<Name>.rgba embedded in the mod.

python scripts/draw_modifier_icons.py
"""
import math
import os

from PIL import Image, ImageDraw

S = 256  # drawn large, downscaled for smooth edges
OUT = os.path.join(os.path.dirname(__file__), '..', 'src', 'plugin', 'Assets', 'Modifiers')
INK = (24, 18, 30, 255)
W = 14  # outline width


def badge(color):
    img = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle((8, 8, S - 8, S - 8), radius=48, fill=INK)
    d.rounded_rectangle((20, 20, S - 20, S - 20), radius=40, fill=color)
    d.rounded_rectangle((20, 20, S - 20, S // 2), radius=40, fill=tuple(min(255, c + 28) for c in color[:3]) + (255,))
    d.rectangle((20, S // 2 - 40, S - 20, S // 2), fill=tuple(min(255, c + 28) for c in color[:3]) + (255,))
    d.rounded_rectangle((20, S // 2, S - 20, S - 20), radius=40, fill=color)
    d.rectangle((20, S // 2, S - 20, S // 2 + 40), fill=color)
    return img, d


def poly(d, pts, fill):
    d.polygon(pts, fill=INK)
    cx = sum(p[0] for p in pts) / len(pts)
    cy = sum(p[1] for p in pts) / len(pts)
    inner = [(cx + (x - cx) * 0.82, cy + (y - cy) * 0.82) for x, y in pts]
    d.polygon(inner, fill=fill)


def circle(d, cx, cy, r, fill, outline=True):
    if outline:
        d.ellipse((cx - r - W, cy - r - W, cx + r + W, cy + r + W), fill=INK)
    d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=fill)


def glass_cannon(d):
    # cannon barrel with a crack, wheels
    d.rounded_rectangle((52, 92, 206, 150), radius=22, fill=INK)
    d.rounded_rectangle((64, 104, 194, 138), radius=14, fill=(190, 235, 255))
    d.line((120, 104, 132, 122, 118, 138), fill=INK, width=8)
    circle(d, 92, 168, 30, (120, 80, 50))
    circle(d, 92, 168, 10, INK, outline=False)


def horde(d):
    for cx, cy, r in ((84, 120, 34), (172, 120, 34), (128, 164, 42)):
        circle(d, cx, cy, r, (150, 220, 120))
        d.ellipse((cx - r * 0.45 - 6, cy - 8, cx - r * 0.45 + 6, cy + 4), fill=INK)
        d.ellipse((cx + r * 0.45 - 6, cy - 8, cx + r * 0.45 + 6, cy + 4), fill=INK)


def thick_skinned(d):
    poly(d, [(128, 44), (204, 74), (196, 150), (128, 214), (60, 150), (52, 74)], (170, 170, 185))
    d.line((128, 70, 128, 190), fill=INK, width=8)


def frenzy(d):
    poly(d, [(146, 36), (72, 140), (122, 140), (104, 220), (186, 106), (134, 106)], (255, 210, 60))


def lucky(d):
    d.line((128, 120, 166, 214), fill=INK, width=26)
    d.line((128, 120, 166, 214), fill=(60, 150, 60), width=10)
    for dx, dy in ((0, -38), (38, 0), (0, 38), (-38, 0)):
        circle(d, 128 + dx, 110 + dy, 32, (90, 200, 90))
    for dx, dy in ((0, -38), (38, 0), (0, 38), (-38, 0)):
        d.ellipse((128 + dx - 32, 110 + dy - 32, 128 + dx + 32, 110 + dy + 32), fill=(90, 200, 90))
    circle(d, 128, 110, 10, (60, 150, 60), outline=False)


def poverty(d):
    circle(d, 128, 128, 66, (200, 160, 60))
    d.ellipse((92, 92, 164, 164), outline=INK, width=8)
    d.line((70, 70, 186, 186), fill=(200, 40, 40), width=22)
    d.line((70, 70, 186, 186), fill=INK, width=6)


def colossus(d):
    # big fist: palm + knuckles
    d.rounded_rectangle((60, 104, 196, 206), radius=26, fill=INK)
    d.rounded_rectangle((72, 116, 184, 194), radius=18, fill=(230, 170, 130))
    for i in range(4):
        x = 66 + i * 32
        d.rounded_rectangle((x, 58, x + 36, 128), radius=14, fill=INK)
        d.rounded_rectangle((x + 8, 66, x + 28, 120), radius=8, fill=(230, 170, 130))


def brittle_swarm(d):
    for cx, cy in ((80, 86), (176, 86), (80, 170), (176, 170), (128, 128)):
        circle(d, cx, cy, 24, (220, 220, 200))
        d.line((cx - 10, cy - 18, cx + 2, cy, cx - 6, cy + 18), fill=INK, width=5)


def bloodbath(d):
    poly(d, [(128, 40), (178, 128), (186, 160), (170, 196), (128, 214), (86, 196), (70, 160), (78, 128)], (210, 30, 40))
    d.ellipse((96, 140, 120, 176), fill=(255, 120, 120))


def pinball(d):
    circle(d, 128, 128, 44, (200, 200, 220))
    d.ellipse((104, 102, 124, 122), fill=(255, 255, 255))
    for ang in (0, 120, 240):
        a = math.radians(ang - 30)
        x1, y1 = 128 + math.cos(a) * 64, 128 + math.sin(a) * 64
        x2, y2 = 128 + math.cos(a) * 100, 128 + math.sin(a) * 100
        d.line((x1, y1, x2, y2), fill=INK, width=14)


def silver_rush(d):
    circle(d, 104, 140, 52, (200, 205, 215))
    circle(d, 156, 112, 52, (225, 230, 240))
    d.ellipse((126, 82, 186, 142), outline=INK, width=6)
    d.line((52, 60, 76, 84), fill=INK, width=10)
    d.line((196, 186, 214, 204), fill=INK, width=10)


def overdrive(d):
    for x in (54, 118):
        poly(d, [(x, 60), (x + 70, 128), (x, 196), (x + 30, 196), (x + 100, 128), (x + 30, 60)], (90, 200, 255))


def shockwave(d):
    for r, w in ((90, 14), (62, 14), (34, 14)):
        d.ellipse((128 - r - 6, 128 - r - 6, 128 + r + 6, 128 + r + 6), outline=INK, width=w + 10)
        d.ellipse((128 - r, 128 - r, 128 + r, 128 + r), outline=(190, 120, 255), width=w)
    circle(d, 128, 128, 14, (255, 255, 255))


def rising_tide(d):
    pts = [(40, 200)] + [(40 + i * 8, 170 + 14 * math.sin(i * 0.6)) for i in range(23)] + [(216, 200)]
    d.polygon(pts + [(216, 216), (40, 216)], fill=INK)
    d.polygon([(48, 196)] + [(48 + i * 7.5, 178 + 12 * math.sin(i * 0.6)) for i in range(23)] + [(212, 196), (212, 206), (48, 206)], fill=(60, 140, 230))
    poly(d, [(128, 40), (176, 96), (148, 96), (148, 150), (108, 150), (108, 96), (80, 96)], (120, 230, 140))


ICONS = {
    'GlassCannon': ((70, 150, 200, 255), glass_cannon),
    'Horde': ((90, 120, 70, 255), horde),
    'ThickSkinned': ((90, 90, 110, 255), thick_skinned),
    'Frenzy': ((190, 70, 40, 255), frenzy),
    'Lucky': ((40, 110, 60, 255), lucky),
    'Poverty': ((110, 90, 60, 255), poverty),
    'Colossus': ((130, 80, 60, 255), colossus),
    'BrittleSwarm': ((120, 110, 90, 255), brittle_swarm),
    'Bloodbath': ((90, 20, 30, 255), bloodbath),
    'Pinball': ((60, 60, 140, 255), pinball),
    'SilverRush': ((80, 90, 110, 255), silver_rush),
    'Overdrive': ((30, 70, 130, 255), overdrive),
    'Shockwave': ((80, 40, 130, 255), shockwave),
    'RisingTide': ((30, 60, 110, 255), rising_tide),
}

os.makedirs(OUT, exist_ok=True)
for name, (color, draw) in ICONS.items():
    img, d = badge(color)
    draw(d)
    icon = img.resize((64, 64), Image.LANCZOS)
    icon.save(os.path.join(OUT, name + '.png'))
    # Embedded in the mod: raw RGBA32 rows bottom first (Texture2D.LoadRawTextureData)
    with open(os.path.join(OUT, name + '.rgba'), 'wb') as f:
        f.write(icon.transpose(Image.FLIP_TOP_BOTTOM).tobytes())
print(len(ICONS), 'icons ->', os.path.abspath(OUT))
