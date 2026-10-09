# Cartur's Compass and Clock

BepInEx mod for Valheim. Adds a Skyrim-style horizontal compass bar at the
top of the screen showing facing direction (N/NE/E/... plus a minor tick
every 15 degrees) and nearby map pins positioned by real-world bearing.

## How it works

Almost all runtime UI. There is exactly one Harmony patch — a postfix on
`TombStone.UpdateDespawn` (`GraveLootedPatch`), which is how the compass
learns a grave has been emptied so its marker can stop showing. Everything
else below reads the game's state without patching it:

- A `Canvas` (screen-space overlay) with a compass bar is built once in
  `Plugin.Awake` and kept alive with `DontDestroyOnLoad`. Layout is baked
  into that hierarchy, so `Config.SettingChanged` queues a full rebuild
  (destroy the canvas, clear the marker pool, build again) on the next
  frame — config edits apply live, without a restart.
- The whole canvas is switched off while the HUD is user-hidden
  (`Hud.IsUserHidden`), the large map is open (`Minimap.m_mode`), or the
  inventory or game menu is up (`InventoryGui.IsVisible`, `Menu.IsVisible`).
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
- Pins are also skipped when they are checked off on the map (`m_checked`),
  when they are a Cartur's Map Pins looted-chest pin (that mod's configured
  looted icon, read from its own config - absent mod, nothing hidden), and
  when they are within 8m: at that range the XZ bearing is sub-metre wobble
  swinging through a full circle, so the marker whipped across the compass
  every frame at max icon scale.
- Both distance cutoffs get 10% slack on release (`RangeSlack`), so a pin
  parked exactly on an edge is not destroyed and rebuilt every frame.
- Each marker's icon is a separate child of the pooled marker (not the same
  `Image` the whole marker root uses) so it can scale and fade with distance
  - 1x and 45% alpha at `PinRange`, 2x and opaque at distance 0 - without
  moving the marker root, whose anchored position is what the bearing math
  writes to.
- Markers live in their own `Markers` container and are re-sorted by sibling
  index every frame, farthest first, so the nearest (largest) icon draws on
  top. Sibling index is only written when it actually changed - reordering
  dirties the canvas.
- The `RectMask2D` on the viewport carries a horizontal `softness`, so
  markers fade out at the window edges instead of being sliced mid-icon.
- Only one pin name is shown: the pin nearest the center tick, in a label
  under the frame. It cannot hang off the marker itself - the window is
  about 24 reference pixels tall, so any text below a marker falls outside
  the mask and is clipped away entirely.

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

Fixed, and deliberately **not** tied to any other UI. `FrameWidth` (657),
`FrameOffsetX` (0, i.e. screen-centered) and `FrameOffsetY` (54 from the top
of the screen) are in reference-resolution pixels against the `CanvasScaler`'s
1920x1080 basis with `ScaleWithScreenSize`, so the compass lands in the same
relative spot at any resolution. Height follows the frame image's aspect
ratio, and the clock is glued directly above the frame's top edge.

The defaults are `FrameWidth` 657, `FrameOffsetX` 0 and `FrameOffsetY` 54.
Where those particular numbers came from is not recorded anywhere in this
repo, so nothing here claims a provenance for them.

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
`BepInEx/config/com.jekkle.valheim.carturcompassandclock.cfg`:

- `PinRange` (float, default 300) — meters. Pins further than this don't show.
- `FieldOfView` (float, default 90) — total degrees of heading visible
  across the window.
- `FrameWidth` (int, default 657) — frame width in reference-resolution
  pixels (1920x1080 basis). Height follows the image's aspect ratio.
- `FrameOffsetX` (float, default 0) — distance right of screen centre to the
  middle of the frame, same units. Negative moves it left.
- `FrameOffsetY` (float, default 54) — distance from the top of the screen
  down to the frame's top edge, same units. The clock rides above the frame.
- `ShowPinNames` (bool, default true) — show the name and distance of the
  pin nearest the center of the compass, in a label under the frame; off
  shows icons only.
- `ShowClock` (bool, default true) — off hides the clock and leaves the compass.
- `TwelveHourClock` (bool, default true) — clock reads `1:05 PM`; off gives
  `13:05`.
- `ClockTextSize` (int, default 26) — size of the clock text, 10 to 60, separate
  from the frame size.
- `Opacity` (float, default 1) — how solid the compass and clock are; lower lets
  the game show through.
- `EditMode` (bool, default false) — draws a box around the compass and lets
  you drag it to move it, or drag the grip on its right edge to resize it.
  The compass stays on screen while this is on, even in menus, and where you
  drop it is written back to the three `Frame*` settings. Turn it off when
  you're happy with it.

Note that BepInEx keeps existing values in an already-generated config file,
so bumping a default in code does **not** move an installed copy — edit the
`.cfg` (or delete it to regenerate) when changing layout defaults.

---

**[Discord](https://discord.gg/nd5RqpwNkz)** — bug reports, install help, and mod requests.
Bug reports get their own thread so nothing is lost in a chat scroll, and requests are voted on.

