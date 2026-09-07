# SkyrimCompass

BepInEx mod for Valheim. Adds a Skyrim-style horizontal compass bar at the
top of the screen showing facing direction (N/E/S/W) and nearby map pins
positioned by real-world bearing.

## How it works

Pure runtime UI, no Harmony patches:

- A `Canvas` (screen-space overlay) with a compass bar is built once in
  `Plugin.Awake` and kept alive with `DontDestroyOnLoad`.
- Each frame, `GameCamera.instance.transform.eulerAngles.y` gives the
  player's facing heading. Cardinal letters and pin icons are placed by
  `Mathf.DeltaAngle(heading, bearing)` — the signed angular offset from
  where you're looking — mapped linearly across the bar width, and hidden
  when outside the configured field of view.
- Pin data comes from `Minimap`'s private `m_pins` list, read via one cached
  reflection `FieldInfo` (no public accessor exists, and no patch is needed
  since we only read it). Each `Minimap.PinData` already carries the same
  `Sprite` used on the in-game map, reused directly for the compass icon.
- Pins beyond `PinRange` are skipped; markers are pooled (created/destroyed
  as pins enter/exit range, shown/hidden as they enter/exit the FOV window)
  rather than rebuilt from scratch every frame.

## Build

Requires .NET 8 SDK and a Valheim install with BepInEx already installed.

```
cd src
dotnet build
```

Managed DLLs for compiling are read from the raw Steam install
(`VALHEIM_INSTALL`). The built plugin deploys to the r2modman `Default`
profile (`R2MODMAN_PROFILE`) — override either if yours differs:

```
dotnet build -p:VALHEIM_INSTALL="D:\SteamLibrary\steamapps\common\Valheim" -p:R2MODMAN_PROFILE="C:\Users\you\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\MyProfile"
```

After building, fully quit Valheim through r2modman and relaunch — BepInEx
only scans plugins on startup.

## Frame art

`Assets/compass_frame.png` is a Norse rune-frame photo, alpha-keyed so only
the carved wood/metal is opaque — both the photo's black backdrop and the
frame's inner window go transparent, letting the compass content show
through the window and nothing show outside the frame's silhouette. The
window's position is hardcoded in `Plugin.cs` (`WinXMin/Max`, `WinYMin/Max`)
as measured fractions of the full frame image, found by scanning outward
from image center for the wood/metal edge (see git history for the
PowerShell used).

`Texture2D.LoadImage`'s byte[] overload internally needs
`System.ReadOnlySpan<byte>`, which doesn't cleanly resolve against
net472 + this game's `netstandard.dll` (`CS0518: Predefined type
'System.ReadOnlySpan\`1' is not defined or imported`). Rather than fight
that, the frame ships as `Assets/compass_frame.rgba` — a raw RGBA32 dump
(8-byte width/height header + bottom-up pixel data) loaded at runtime via
the old `Texture2D.SetPixels32`, which has no Span overload to trip over.

To regenerate `compass_frame.rgba` after editing `compass_frame.png`:

```powershell
Add-Type -AssemblyName System.Drawing
$src = [System.Drawing.Bitmap]::FromFile("D:\Ai\valheim-compass-mod\src\Assets\compass_frame.png")
$w = $src.Width; $h = $src.Height
$fs = [System.IO.File]::Create("D:\Ai\valheim-compass-mod\src\Assets\compass_frame.rgba")
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([int32]$w); $bw.Write([int32]$h)
for ($y = $h - 1; $y -ge 0; $y--) {
    for ($x = 0; $x -lt $w; $x++) {
        $p = $src.GetPixel($x, $y)
        $bw.Write([byte]$p.R); $bw.Write([byte]$p.G); $bw.Write([byte]$p.B); $bw.Write([byte]$p.A)
    }
}
$bw.Close(); $fs.Close(); $src.Dispose()
```

(Note: if re-deriving alpha from a fresh non-transparent source photo, apply
the brightness-ramp alpha-key first — see git history for the exact
thresholds used.)

## Config

After first run, edit
`BepInEx/config/com.jekkle.valheim.skyrimcompass.cfg`:

- `PinRange` (float, default 300) — meters. Pins further than this don't show.
- `FieldOfView` (float, default 90) — total degrees of heading visible
  across the window.
- `FrameWidth` (int, default 700) — pixels; height follows the frame image's
  aspect ratio automatically.
- `ShowPinNames` (bool, default true) — show pin name + distance text under
  each icon; off shows icons only.

## Untested

Not yet launch-tested in game. Next step: fully restart Valheim via
r2modman, confirm the plugin loads (check
`%APPDATA%\r2modmanPlus-local\Valheim\profiles\Default\BepInEx\LogOutput.log`
for "SkyrimCompass 1.0.0 loaded."), and verify the bar renders, turns with
the camera, and pins line up with their real direction.
