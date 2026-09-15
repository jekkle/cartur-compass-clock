"""Converts a frame PNG to the raw RGBA32 dump Plugin.cs loads.

Texture2D.LoadImage's byte[] overload wants System.ReadOnlySpan<byte>, which does not
resolve against net472 plus this game's netstandard.dll, so the frame ships as raw
pixels instead: an 8-byte header (width, height as little-endian int32) followed by
RGBA bytes in bottom-up row order, which is what Texture2D.SetPixels32 expects.

    python tools/make_rgba.py src/Assets/compass_frame.png
"""
import struct
import sys

from PIL import Image

source = sys.argv[1]
out = source[:-4] + ".rgba" if source.lower().endswith(".png") else source + ".rgba"

image = Image.open(source).convert("RGBA")
# PIL hands rows top-down; SetPixels32 indexes from the bottom-left.
bottom_up = image.transpose(Image.FLIP_TOP_BOTTOM)

with open(out, "wb") as handle:
    handle.write(struct.pack("<ii", image.width, image.height))
    handle.write(bottom_up.tobytes())

print(f"wrote {out} ({image.width}x{image.height})")
