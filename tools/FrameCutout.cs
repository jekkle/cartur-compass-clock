// Asset-prep helper, not part of the mod build. Cuts a frame photo (dark backdrop, carved
// wood/metal frame, dark inner window) into a proper alpha cutout: flood-fills from the image
// border and a handful of seed points inside the known window rect, treating reached pixels as
// background (alpha 0) and everything else - the frame silhouette itself - as fully opaque
// (alpha 255), regardless of how dark individual wood/moss pixels are. That "connectivity" test
// is what the compass's earlier brightness-ramp attempt got wrong: dark carved wood pixels
// landed in the same brightness range as the window backdrop, so a plain per-pixel threshold
// left the wood only ~80% opaque - a translucent "ghost frame" look over bright sky.
//
// Compile and run via PowerShell:
//   $src = Get-Content -Raw tools\FrameCutout.cs
//   Add-Type -TypeDefinition $src -ReferencedAssemblies System.Drawing.dll -OutputAssembly tools\FrameCutout.dll -OutputType Library
//   Add-Type -Path tools\FrameCutout.dll
//   [FrameCutout]::Process(
//       "<path to source frame jpg/png>",
//       "src\Assets\compass_frame.rgba",
//       "src\Assets\compass_frame.png",
//       @(<x1>,<y1>, <x2>,<y2>, ...)   # a handful of points known to sit inside the window
//   )
//
// Output is a raw RGBA32 dump (8-byte width/height header + bottom-up pixel data, not a PNG -
// see the .csproj comment for why Plugin.cs doesn't decode a PNG at runtime) plus a normal
// top-down reference PNG for eyeballing the result.

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Collections.Generic;
using System.IO;

public static class FrameCutout
{
    // blackThreshold: max(R,G,B) below this counts as background (dark backdrop / dark window).
    // whiteThreshold: min(R,G,B) above this counts as background (white backdrop). Set to 999
    // to disable one side - e.g. an all-black-backdrop photo only needs blackThreshold.
    public static void Process(string inPath, string outRgbaPath, string outPngPath, int[] seeds,
        int blackThreshold = 38, int whiteThreshold = 999)
    {
        Bitmap bmp = new Bitmap(inPath);
        int w = bmp.Width, h = bmp.Height;
        BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        int stride = bd.Stride;
        byte[] raw = new byte[stride * h];
        System.Runtime.InteropServices.Marshal.Copy(bd.Scan0, raw, 0, raw.Length);
        bmp.UnlockBits(bd);

        byte[] r = new byte[w * h], g = new byte[w * h], b = new byte[w * h];
        for (int y = 0; y < h; y++)
        {
            int row = y * stride;
            for (int x = 0; x < w; x++)
            {
                int idx = y * w + x;
                r[idx] = raw[row + x * 3 + 2];
                g[idx] = raw[row + x * 3 + 1];
                b[idx] = raw[row + x * 3];
            }
        }

        // 3x3 box blur per channel so JPEG noise doesn't fragment the flood fill.
        Func<byte[], byte[]> blur = channel =>
        {
            byte[] result = new byte[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int sum = 0, cnt = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int yy = y + dy;
                        if (yy < 0 || yy >= h) continue;
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int xx = x + dx;
                            if (xx < 0 || xx >= w) continue;
                            sum += channel[yy * w + xx];
                            cnt++;
                        }
                    }
                    result[y * w + x] = (byte)(sum / cnt);
                }
            }
            return result;
        };
        byte[] br = blur(r), bg = blur(g), bb = blur(b);

        // Background is either near-black (dark backdrop/window) or near-white (light backdrop) -
        // wood/metal sits in the middle brightness range and satisfies neither.
        bool[] isBg = new bool[w * h];
        for (int i = 0; i < w * h; i++)
        {
            int mx = Math.Max(br[i], Math.Max(bg[i], bb[i]));
            int mn = Math.Min(br[i], Math.Min(bg[i], bb[i]));
            isBg[i] = (mx < blackThreshold) || (mn > whiteThreshold);
        }

        bool[] visited = new bool[w * h];
        Queue<int> queue = new Queue<int>();

        Action<int, int> trySeed = (x, y) =>
        {
            if (x < 0 || x >= w || y < 0 || y >= h) return;
            int idx = y * w + x;
            if (isBg[idx] && !visited[idx]) { visited[idx] = true; queue.Enqueue(idx); }
        };

        for (int x = 0; x < w; x++) { trySeed(x, 0); trySeed(x, h - 1); }
        for (int y = 0; y < h; y++) { trySeed(0, y); trySeed(w - 1, y); }
        for (int i = 0; i < seeds.Length; i += 2) trySeed(seeds[i], seeds[i + 1]);

        int[] dx8 = { -1, 0, 1, -1, 1, -1, 0, 1 };
        int[] dy8 = { -1, -1, -1, 0, 0, 1, 1, 1 };
        while (queue.Count > 0)
        {
            int idx = queue.Dequeue();
            int x = idx % w, y = idx / w;
            for (int k = 0; k < 8; k++)
            {
                int xx = x + dx8[k], yy = y + dy8[k];
                if (xx < 0 || xx >= w || yy < 0 || yy >= h) continue;
                int nidx = yy * w + xx;
                if (isBg[nidx] && !visited[nidx]) { visited[nidx] = true; queue.Enqueue(nidx); }
            }
        }

        using (FileStream fs = File.Create(outRgbaPath))
        using (BinaryWriter bw = new BinaryWriter(fs))
        {
            bw.Write(w); bw.Write(h);
            for (int y = h - 1; y >= 0; y--)
            {
                for (int x = 0; x < w; x++)
                {
                    int idx = y * w + x;
                    byte a = visited[idx] ? (byte)0 : (byte)255;
                    bw.Write(r[idx]); bw.Write(g[idx]); bw.Write(b[idx]); bw.Write(a);
                }
            }
        }

        Bitmap outBmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = y * w + x;
                byte a = visited[idx] ? (byte)0 : (byte)255;
                outBmp.SetPixel(x, y, Color.FromArgb(a, r[idx], g[idx], b[idx]));
            }
        }
        outBmp.Save(outPngPath, ImageFormat.Png);
        bmp.Dispose();
        outBmp.Dispose();
    }
}
