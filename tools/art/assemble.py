"""Grok's compass bars come back at 5:1 to 7.6:1; the frame is 2000x138 (14.5:1) and Plugin.cs
places content by fractions of it. Stretching would smear the end caps and the knotwork, so the
bar is rebuilt at the real size from its own pieces: both end caps and the centre pointer as
drawn, the run between them filled by repeating a slice one tick-spacing wide (the period is
measured by autocorrelation along the window's top rim, so the ticks keep their rhythm).

    python tools/art/assemble.py compass_4.png out.png [--black-window] [--match ref.png ...]

--black-window fills the window recess with flat black and so removes the tick marks (Cartur,
2026-10-05, on option 4: "background black and the tick marks gone"). The recess is the run of
rows where at least 85% of the straight bar is darker than 45 - the charcoal window with the
ticks in it - bounded by the bronze bevel rows either side; its width is the dark run on the
middle row. Measured each time, not hard-coded.

--match recolours the metal to the UI's (Cartur, 2026-10-05: "color match it to the rest of the
ui"): per channel, mean and spread of the bar's metal moved onto those of the references' metal -
the method cartur-ui-hud/tools/art/fit2.py used on the equipment board. Metal = opaque pixels
brighter than 40 and not teal (runes); the black window and the background are left alone.

White around the bar becomes transparent (flood from the edge, as cartur-ui-hud's boards.py).
"""
import sys
import numpy as np
from PIL import Image
from scipy import ndimage

W, H = 2000, 138

def cut_white(rgb):
    lum = rgb.mean(axis=2)
    labels, _ = ndimage.label(lum > 235)
    edge = np.unique(np.concatenate([labels[0], labels[-1], labels[:, 0], labels[:, -1]]))
    bg = np.isin(labels, edge[edge > 0])
    alpha = np.where(bg, 0, 255).astype(np.uint8)
    rim = ndimage.binary_dilation(bg, iterations=2) & ~bg
    alpha[rim] = np.clip((255 - lum[rim]) / (255 - 90) * 255, 0, 255).astype(np.uint8)
    return np.dstack([rgb, alpha])

def main(src, dst, black_window=False, match=()):
    rgb = np.asarray(Image.open(src).convert("RGB"))
    a = rgb.mean(axis=2) < 235
    ys, xs = np.where(a)
    bar = rgb[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
    h0, w0 = bar.shape[:2]
    k = H / h0
    bar = np.asarray(Image.fromarray(bar).resize((round(w0 * k), H), Image.LANCZOS))
    w = bar.shape[1]

    # Ends kept as drawn: the cap plus the window's own corner, i.e. everything up to 20% of the
    # bar in from each side. The run that repeats is cut from the straight middle (25-45%), so no
    # window edge or corner is ever in it.
    cap_l = int(w * 0.20)
    cap_r = w - cap_l
    mid = w // 2
    ptr = 40   # half-width kept around the centre pointer
    t0 = int(w * 0.25)

    # Repeat period from the knotwork band in the top rim (12% down), searched 60-300 px so wood
    # grain cannot win; the band is the most visible seam.
    row = bar[int(H * 0.12), t0:int(w * 0.45)].mean(axis=1)
    row = row - row.mean()
    ac = np.correlate(row, row, "full")[len(row) - 1:]
    period = int(np.argmax(ac[60:300]) + 60)

    # Seams: start the slice where its two ends match best (searched over the straight run), then
    # cross-fade BLEND px across every join - Grok lights the bar unevenly along its length, so a
    # hard cut showed as a brightness step at each repeat.
    BLEND = 16
    f = bar.astype(np.float32)
    best, t0 = None, t0
    for t in range(int(w * 0.22), int(w * 0.45) - period - BLEND):
        err = np.abs(f[:, t] - f[:, t + period]).mean()
        if best is None or err < best:
            best, t0 = err, t
    tile = f[:, t0:t0 + period + BLEND]
    ramp = np.linspace(0, 1, BLEND)[None, :, None]

    out = np.zeros((H, W, 3), np.float32)
    out[:, :cap_l] = f[:, :cap_l]
    c0 = W // 2 - ptr
    end = W - (w - cap_r)

    def lay(x0, x1):
        # Each repeat starts BLEND px early, over whatever is already there (the cap, the pointer or
        # the previous repeat), and fades in across it. Overrunning x1 is fine: the pointer and the
        # right end are placed on top afterwards.
        x = x0 - BLEND
        while x < x1:
            n = min(period + BLEND, W - x)
            piece = tile[:, :n].copy()
            piece[:, :BLEND] = out[:, x:x + BLEND] * (1 - ramp) + piece[:, :BLEND] * ramp
            out[:, x:x + n] = piece
            x += period

    lay(cap_l, c0)
    # Pointer and right end go on last, each faded in over BLEND px from what is under it.
    def place(x, src):
        n = src.shape[1]; k = min(BLEND, n)
        src = src.copy()
        src[:, :k] = out[:, x:x + k] * (1 - ramp[:, :k]) + src[:, :k] * ramp[:, :k]
        out[:, x:x + n] = src
    place(c0, f[:, mid - ptr:mid + ptr])
    lay(c0 + 2 * ptr, end)
    place(end, f[:, cap_r:])
    out = out.clip(0, 255).astype(np.uint8)

    if black_window:
        lum = out.mean(axis=2)
        run = lum[:, int(W * 0.15):int(W * 0.45)]
        dark = (run < 45).mean(axis=1) >= 0.85
        cy = H // 2
        y0 = cy
        while y0 > 0 and dark[y0 - 1]: y0 -= 1
        y1 = cy
        while y1 < H - 1 and dark[y1 + 1]: y1 += 1
        # Width: the unbroken dark run outward from the centre on the middle row - the end caps
        # have dark pixels of their own, so "any dark column" reached the image edge.
        x0 = x1 = W // 2
        while x0 > 0 and lum[cy, x0 - 1] < 45: x0 -= 1
        while x1 < W - 1 and lum[cy, x1 + 1] < 45: x1 += 1
        out[y0:y1 + 1, x0:x1 + 1] = 0
        print(f"  black window rows {y0}..{y1}, cols {x0}..{x1}")

    rgba = cut_white(out)
    if match:
        def metal(px):
            rgb, al = px[..., :3].astype(np.float64), px[..., 3]
            lum = rgb.mean(axis=2)
            teal = (rgb[..., 1] > rgb[..., 0] + 10) & (rgb[..., 2] > rgb[..., 0] + 10)
            return (al > 200) & (lum > 40) & ~teal
        ref = np.concatenate([np.asarray(Image.open(r).convert("RGBA"))[metal(np.asarray(Image.open(r).convert("RGBA")))][:, :3]
                              for r in match]).astype(np.float64)
        m = metal(rgba)
        pix = rgba[..., :3].astype(np.float64)
        for c in range(3):
            ch = pix[..., c]
            sm, ss = ch[m].mean(), ch[m].std() or 1.0
            rm, rs = ref[:, c].mean(), ref[:, c].std()
            ch[m] = np.clip((ch[m] - sm) * (rs / ss) + rm, 0, 255)
        print(f"  metal matched to {len(match)} reference(s): mean {ref.mean(axis=0).round(1)}")
        rgba = np.dstack([pix.round().astype(np.uint8), rgba[..., 3]])
    Image.fromarray(rgba, "RGBA").save(dst)
    print(f"{src}: bar {w0}x{h0} -> caps {cap_l}/{w - cap_r} px, tick period {period} px -> {dst} {W}x{H}")

if __name__ == "__main__":
    refs = sys.argv[sys.argv.index("--match") + 1:] if "--match" in sys.argv else []
    main(sys.argv[1], sys.argv[2], "--black-window" in sys.argv, [r for r in refs if not r.startswith("--")])
