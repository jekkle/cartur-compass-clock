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

`Assets/compass_frame.png` already ships with correct alpha — only the
outer silhouette is transparent. The window interior is deliberately left
as **opaque baked-in black**, not a cutout: `Plugin.cs` layers `FrameArt`
(the frame image) behind `Content` (ticks/cardinal letters/pin markers), so
the dynamic compass content draws on top of that black backdrop rather than
showing through a transparent hole. The window's position is still needed
to keep `Content` positioned inside it — hardcoded in `Plugin.cs`
(`WinXMin/Max`, `WinYMin/Max`) as measured fractions of the full frame
image (a brightness scan, since even though this PNG's alpha is already
correct, its window/wood boundary still has to be measured to know where
`Content` goes).

`Texture2D.LoadImage`'s byte[] overload internally needs
`System.ReadOnlySpan<byte>`, which doesn't cleanly resolve against
net472 + this game's `netstandard.dll` (`CS0518: Predefined type
'System.ReadOnlySpan\`1' is not defined or imported`). Rather than fight
that, the frame ships as `Assets/compass_frame.rgba` — a raw RGBA32 dump
(8-byte width/height header + bottom-up pixel data) loaded at runtime via
the old `Texture2D.SetPixels32`, which has no Span overload to trip over.
When the source PNG already has correct alpha (as this one does), exporting
it is a direct pixel copy — no flood fill or thresholding needed.

`tools/FrameCutout.cs` is still here for the case where a *future* source
photo does need its background removed (a flat-color backdrop with no
alpha channel of its own) — a flood fill from the image border plus a few
seed points inside the window rect, using `blackThreshold`/`whiteThreshold`
depending on whether that photo's backdrop is black or white (999 disables
the side that doesn't apply). Not used for the current frame art.

## Sizing

The frame's on-screen width auto-matches the vanilla hotbar (item slots)
every second, via `HotkeyBar`'s `RectTransform.GetWorldCorners` compared
through this mod's own canvas `scaleFactor` — re-checked continuously
rather than once, since mods like EquipmentAndQuickSlots can change the
hotbar's slot count/width at runtime. `FrameWidth` in config is only the
initial/fallback size used before the hotbar is found.

## Config

After first run, edit
`BepInEx/config/com.jekkle.valheim.skyrimcompass.cfg`:

- `PinRange` (float, default 300) — meters. Pins further than this don't show.
- `FieldOfView` (float, default 90) — total degrees of heading visible
  across the window.
- `FrameWidth` (int, default 700) — pixels; initial/fallback size only, see
  Sizing above. Height follows the frame image's aspect ratio automatically.
- `ShowPinNames` (bool, default true) — show pin name + distance text under
  each icon; off shows icons only.

## Untested

Not yet launch-tested in game. Next step: fully restart Valheim via
r2modman, confirm the plugin loads (check
`%APPDATA%\r2modmanPlus-local\Valheim\profiles\Default\BepInEx\LogOutput.log`
for "SkyrimCompass 1.0.0 loaded."), and verify the bar renders, turns with
the camera, and pins line up with their real direction.
