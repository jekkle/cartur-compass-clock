# Cartur's Compass and Clock

*Free, and always will be — if it improved your game you can [tip me on Patreon](https://www.patreon.com/c/cartur).*

**More from Cartur:** [HD Blood](https://thunderstore.io/c/valheim/p/Cartur/Carturs_HD_Blood/) ·
[Map Pins](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Map_Pins/) ·
[Safe Stamina](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Safe_Stamina/)

A Skyrim-style compass bar across the top of the screen.

Your heading runs across a carved wooden bar — N, NE, E and the rest, with a
tick every 15° between them — and every map pin within range sits on it at its
real bearing, so a pin at your two o'clock appears at your two o'clock.

## What it does

- **Pins at their true bearing.** Read straight from the in-game map, so
  whatever you have pinned is what you see, with the same icons.
- **Distance you can read at a glance.** Icons grow and brighten as you close
  on them: small and faded at the edge of range, twice the size and fully
  opaque when you are nearly on top of them.
- **One name at a time.** The pin nearest the centre of the compass gets its
  name and distance under the bar. No wall of overlapping text.
- **Stays out of the way.** Hides itself while the big map, your inventory or
  the game menu is open, and when you toggle the HUD off.
- **Clock.** In-game time above the bar, in Valheim's own serif, and it names the
  part of the day with it - `Dawn 42 - 6:40 AM`, `Dusk 42 - 5:20 PM`,
  `Night 43 - 12:15 AM`. Night runs 18:00 to 06:00, the same hours the game's own
  spawns and sleep rules use, so the word tells you whether it is safe out without
  doing the arithmetic. Dusk is the two hours before dark, as a warning.

### Pins it deliberately hides

- Pins you have checked off on the map — you are done with those.
- Chests emptied out, if you also run **Cartur's Map Pins** — it reads that
  mod's own looted-chest icon setting, so cleared chests drop off the compass
  while staying on your map. Not installed? Nothing changes.
- Anything within 8 metres. A pin you are standing on has no meaningful
  direction, and it would otherwise swing wildly across the bar as you move.

## Install

Use a mod manager (r2modman / Thunderstore / Gale) — it will pull in
BepInEx for you.

Manually: drop `CarturCompassAndClock.dll` into `BepInEx/plugins`.

## Config

`BepInEx/config/com.jekkle.valheim.carturcompassandclock.cfg`, written on first run.
Changes apply live — no restart needed.

| Setting | Default | What it does |
| --- | --- | --- |
| `PinRange` | 300 | Metres. Pins further out don't show. |
| `FieldOfView` | 90 | Total degrees of heading visible across the bar. |
| `ShowPinNames` | true | Name + distance of the centred pin. Off = icons only. |
| `TwelveHourClock` | true | `1:05 PM`. Off = 24-hour, `13:05`. |
| `FrameWidth` | 657 | Bar width, on a 1920x1080 basis. Scales with your resolution. |
| `FrameOffsetX` | 0 | Distance right of screen centre. Negative moves it left, same units. |
| `FrameOffsetY` | 54 | Gap from the top of the screen down to the bar, same units. |
| `EditMode` | false | Drag the compass around the screen. See below. |

### Moving and resizing it

Rather than working the numbers out by hand:

1. Turn on `EditMode` in the config manager.
2. **Open your inventory.** You need a free cursor - without a menu open the mouse
   is driving the camera, and clicking swings your weapon.
3. Drag the box to move it. Drag the grip on its right edge to resize it.
4. Turn `EditMode` back off.

Where you drop it is written straight back to `FrameWidth`, `FrameOffsetX` and
`FrameOffsetY`, so it survives restarts and you can still fine-tune the numbers by
hand afterwards.

## Compatibility

No Harmony patches — the compass is its own UI canvas that reads the map's pin
list. It doesn't touch the map, the HUD, or anyone else's UI, so it should sit
alongside other mods without argument.

Single-player and multiplayer, client-side only. The server does not need it,
and other players do not need it.
