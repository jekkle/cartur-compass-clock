"""Generates the 256x256 Thunderstore icon from the frame art.

Thunderstore rejects anything that is not exactly 256x256 PNG, so this is scripted
rather than hand-cropped: rerun it if Assets/compass_frame.png changes.

    python tools/make_icon.py
"""
import os

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
FRAME = os.path.join(ROOT, "src", "Assets", "compass_frame.png")
OUT = os.path.join(ROOT, "package", "icon.png")

SIZE = 256
GOLD = (255, 214, 120)
DIM_GOLD = (196, 158, 88)

icon = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 255))
draw = ImageDraw.Draw(icon)

# Vertical gradient, dark wood-ash tones, so the frame art sits on something warmer than black.
for y in range(SIZE):
    t = y / (SIZE - 1)
    draw.line([(0, y), (SIZE, y)], fill=(int(38 - 22 * t), int(33 - 19 * t), int(26 - 15 * t), 255))

# Corner vignette.
vignette = Image.new("L", (SIZE, SIZE), 0)
ImageDraw.Draw(vignette).ellipse([-60, -60, SIZE + 60, SIZE + 60], fill=255)
icon = Image.composite(icon, Image.new("RGBA", (SIZE, SIZE), (8, 7, 5, 255)), vignette)
draw = ImageDraw.Draw(icon)

# A zoomed slice of the real bar, not the whole 6.9:1 frame - at full width the window
# would be a few pixels tall and nothing inside it would read at 256px.
frame = Image.open(FRAME).convert("RGBA")
slice_w = round(frame.width * 0.24)
left = (frame.width - slice_w) // 2
bar = frame.crop((left, 0, left + slice_w, frame.height))
bar_h = round(SIZE * bar.height / bar.width)
bar = bar.resize((SIZE, bar_h), Image.LANCZOS)
bar_y = (SIZE - bar_h) // 2
icon.alpha_composite(bar, (0, bar_y))

# Window bounds, same measured fractions the plugin uses.
win_top = bar_y + round(0.3103 * bar_h)
win_bottom = bar_y + round(0.6092 * bar_h)
win_mid = (win_top + win_bottom) // 2

# What the window actually holds in game: a heading letter, a pin marker, and the centre
# tick marking where you are facing.
font = ImageFont.truetype("C:/Windows/Fonts/arialbd.ttf", 30)
draw.text((168, win_mid), "N", font=font, fill=GOLD, anchor="mm")
draw.regular_polygon((72, win_mid, 11), n_sides=4, rotation=45, fill=DIM_GOLD)
draw.rectangle([SIZE // 2 - 2, win_top + 2, SIZE // 2 + 1, win_bottom - 2], fill=GOLD)

os.makedirs(os.path.dirname(OUT), exist_ok=True)
icon.convert("RGB").save(OUT, "PNG")
print(f"{OUT} {icon.size}")
