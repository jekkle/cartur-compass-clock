# Cartur's Compass and Clock

A compass bar across the top of the screen, with your map pins on it at their real bearing.

![The bar, the clock and the nearest pin's name](https://raw.githubusercontent.com/jekkle/cartur-compass-clock/master/docs/images/compass-hud.png)

![In game](https://raw.githubusercontent.com/jekkle/cartur-compass-clock/master/docs/images/compass-ingame.png)

- A pin at your two o'clock appears at your two o'clock. Read straight from the
  in-game map, with the same icons.
- Icons grow and brighten as you close on them.
- The pin nearest the centre shows its name and distance. One at a time.
- A clock above the bar names the part of the day — `Morning, Day 42 - 6:40 AM`,
  `Night, Day 43 - 12:15 AM` — so you know whether it is safe out without doing
  the arithmetic.
- Hides itself while the map, your inventory or the menu is open.
- Pins you have ticked off don't show, and nor do emptied chests if you also run
  Cartur's Map Pins.

## Settings

`BepInEx/config/com.jekkle.valheim.carturcompassandclock.cfg`. Changes apply live.

| Setting | Default | What it does |
| --- | --- | --- |
| `PinRange` | 300 | Metres. Pins further out don't show. |
| `FieldOfView` | 90 | Degrees of heading visible across the bar. |
| `ShowPinNames` | true | Name and distance of the centred pin. |
| `TwelveHourClock` | true | `1:05 PM`. Off gives `13:05`. |
| `FrameWidth` | 657 | Bar width, on a 1920x1080 basis. Scales with your resolution. |
| `FrameOffsetX` | 0 | Distance right of screen centre. Negative moves it left. |
| `FrameOffsetY` | 54 | Gap from the top of the screen down to the bar. |
| `EditMode` | false | Drag the compass around the screen — see below. |

### Moving and resizing it

1. Turn on `EditMode`.
2. **Open your inventory**, so you have a free cursor.
3. Drag the box to move it. Drag the grip on its right edge to resize it.
4. Turn `EditMode` back off.

Where you drop it is written back to the three layout settings, so it survives
restarts and you can still fine-tune the numbers by hand.

## Install

Use a mod manager (r2modman / Thunderstore / Gale) and it pulls in BepInEx for you.
Manually: drop `CarturCompassAndClock.dll` into `BepInEx/plugins`.

Client-side. The server doesn't need it and neither do the people you play with.

---

*Free, and always will be. If it improved your game you can [tip me on Patreon](https://www.patreon.com/c/cartur).*

**[Discord](https://discord.gg/nd5RqpwNkz)** — bug reports, install help, and mod requests.
Bug reports get their own thread so nothing is lost in a chat scroll, and requests are voted on.

**More from Cartur:**
[HD Blood](https://thunderstore.io/c/valheim/p/Cartur/Carturs_HD_Blood/) ·
[Map Pins](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Map_Pins/) ·
[Safe Stamina](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Safe_Stamina/) ·
[Follow Command](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Follow_Command/) ·
[Flooring](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Flooring/) ·
[UI HUD](https://thunderstore.io/c/valheim/p/Cartur/Carturs_UI_HUD/) ·
[Waste Management](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Waste_Management/) ·
[Feeding Trough](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Feeding_Trough/)
