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
- Each marker's icon is a separate child of the pooled marker (not the same
  `Image` the whole marker root uses) so it can scale with distance -
  1x at `PinRange`, 2x at distance 0 - without also stretching the label's
  size and offset underneath it.

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

## Sizing and position

Fixed, and deliberately **not** tied to any other UI. `FrameWidth` (552)
and `FrameOffsetY` (36 from the top of the screen) are in
reference-resolution pixels against the `CanvasScaler`'s 1920x1080 basis
with `ScaleWithScreenSize`, so the compass lands in the same relative spot
at any resolution. Height follows the frame image's aspect ratio, and the
clock is glued directly above the frame's top edge. Horizontally it's
screen-centered.

Those two numbers were measured from a working in-game layout, not guessed.

### Why it isn't tied to the hotbar

An earlier version tried to auto-match the hotbar's on-screen width and
vertical center via `HotkeyBar`'s `RectTransform.GetWorldCorners`. Don't
reintroduce that — it was abandoned for two solid reasons:

1. **There is more than one `HotkeyBar`, and no reliable way to tell which
   is on screen.** EquipmentAndQuickSlots clones the vanilla `"HotKeyBar"`
   into a second object named `"QuickSlotsHotkeyBar"` and repositions the
   clone. In practice the *vanilla* bar was the visible one (top-left, slots
   1-7) while the clone sat unused at the bottom of the screen — but an
   unused bar still reports a perfectly valid `RectTransform`, so matching
   the wrong one silently parked the compass at the bottom. Both candidates
   reported `active=True` and zero populated child elements, so neither name,
   active state, nor slot count separated them; selection came down to
   `FindObjectsByType` ordering, i.e. luck.
2. **It only works for the mod setup it was tuned against.** Anyone without
   these mods gets a differently-positioned (or differently-numbered)
   hotbar, so a fixed position is both more predictable and more portable.

Logged measurements from that investigation, for reference (2848x1600
screen, canvas `scaleFactor` 1.33):

```
HotkeyBar candidate 'QuickSlotsHotkeyBar': active=True, elements=0, screen y 200..285    <- unused, bottom
HotkeyBar candidate 'HotKeyBar':           active=True, elements=0, screen y 1456..1541  <- visible, top
Syncing to 'HotKeyBar': screen width 736px -> frame 552x80 local, anchoredPosition.y -36
```

## Config

After first run, edit
`BepInEx/config/com.jekkle.valheim.skyrimcompass.cfg`:

- `PinRange` (float, default 300) — meters. Pins further than this don't show.
- `FieldOfView` (float, default 90) — total degrees of heading visible
  across the window.
- `FrameWidth` (int, default 552) — frame width in reference-resolution
  pixels (1920x1080 basis). Height follows the image's aspect ratio.
- `FrameOffsetY` (float, default 36) — distance from the top of the screen
  down to the frame's top edge, same units. The clock rides above the frame.
- `ShowPinNames` (bool, default true) — show pin name + distance text under
  each icon; off shows icons only.

Note that BepInEx keeps existing values in an already-generated config file,
so bumping a default in code does **not** move an installed copy — edit the
`.cfg` (or delete it to regenerate) when changing layout defaults.

## Status

Launch-tested in game: the bar renders, turns with the camera, pins appear
at their real bearing and scale with distance, and the clock tracks in-game
time. Verify a fresh install by checking
`%APPDATA%\r2modmanPlus-local\Valheim\profiles\Default\BepInEx\LogOutput.log`
for "SkyrimCompass 1.0.0 loaded."
