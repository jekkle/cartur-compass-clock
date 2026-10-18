# Cartur's Compass and Clock

A compass bar across the top of the screen with your map pins on it at their real bearing, and a clock above it that names the time of day.

![The bar, the clock and the nearest pin's name](https://raw.githubusercontent.com/jekkle/cartur-compass-clock/master/docs/images/compass-hud.png)

![In game](https://raw.githubusercontent.com/jekkle/cartur-compass-clock/master/docs/images/compass-ingame.png)

## What it does

The bar shows the cardinal and intercardinal letters and a tick every 15 degrees, across a field of view of 90 degrees by default. Pins are read straight from the in-game map and drawn with the same icons, so a pin at your two o'clock appears at your two o'clock. A pin takes the colour it has on the map. If you run Cartur's Map Pins, its per-pin colours and ore tints carry over, and emptied chests are left off the compass. Without that mod you get the vanilla pins in white. Pins further away than `PinRange` (300 metres) are not drawn, and neither are pins you have ticked off on the map or pins you are standing within 8 metres of.

Icons grow and get more opaque as you close on a pin. The pin nearest the centre of the bar, within about 12 degrees, shows its name and distance under the frame, one at a time. Where several pins would pile up on the same spot, the nearest one wins and the ones behind it are skipped for that frame.

Your home bed and your graves are treated differently. They ignore `PinRange`, so they show at any distance, they are drawn larger in a fixed colour (gold for home, red for graves), and they sit on top of the other markers. A grave marker goes away once you have emptied the grave. The graves rule is per session, so a grave emptied before the mod was installed or in an earlier session keeps its marker until you clear it on the map. `DeathMarkers` can limit graves to the pin range or hide them.

The clock sits above the bar and reads like `Morning, Day 42 - 6:40 AM` or `Night, Day 43 - 12:15 AM`. The part of the day is one of Morning, Afternoon or Night, so you can tell whether it is safe out without doing the arithmetic. These words are translated for English, German, Russian, French, Spanish, Italian, Polish, Brazilian Portuguese, Chinese, Japanese and Korean. Any other language shows English.

The whole thing hides itself while the large map, your inventory or the menu is open, when you toggle the HUD off, and during the cutscene after a boss kill.

## Settings

Config file: `BepInEx/config/com.jekkle.valheim.carturcompassandclock.cfg`. Changes apply live.

| Setting | Default | What it does |
| --- | --- | --- |
| `PinRange` | 300 | Metres. Pins further out don't show. |
| `DeathMarkers` | All | Grave markers: All shows every grave on your map at any distance, InRange applies `PinRange`, Off hides them. Your map is not changed. |
| `FieldOfView` | 90 | Degrees of heading visible across the bar. |
| `ShowPinNames` | true | Name and distance of the centred pin. |
| `ShowClock` | true | Off hides the clock and leaves the compass. |
| `ShowDay` | true | Show the time of day and day number with the clock. Off shows the time alone. |
| `TwelveHourClock` | true | `1:05 PM`. Off gives `13:05`. |
| `ClockFormat` | empty | Write your own clock text, for example `{day} \| {time24}` gives `Day 12 \| 13:05`. Tokens: `{phase}` (Morning, Afternoon, Night), `{day}` (Day 12), `{daynum}` (12), `{time}` (follows `TwelveHourClock`), `{time12}` (1:05 PM), `{time24}` (13:05), `{hour}` (13), `{hour12}` (1), `{minute}` (05), `{ampm}` (PM). Empty keeps the text above. Applies straight away. |
| `ClockTextSize` | 26 | Size of the clock text, separate from the bar's size. 10 to 60. |
| `Opacity` | 1 | How solid the compass and clock are, 0.1 to 1. Lower lets the game show through. |
| `Style` | Auto | Bronze is the bronze frame. Rune is a flat dark panel with a thin black border, matching Cartur's RuneUI. Auto uses Rune when Cartur's RuneUI is installed, Bronze otherwise. |
| `FrameWidth` | 657 | Bar width, on a 1920x1080 basis. Scales with your resolution. |
| `FrameOffsetX` | 0 | Distance right of screen centre. Negative moves it left. |
| `FrameOffsetY` | 54 | Gap from the top of the screen down to the bar. |
| `EditMode` | false | Drag the compass around the screen, see below. |

### Moving and resizing it

1. Turn on `EditMode`.
2. Open your inventory, so you have a free cursor.
3. Drag the box to move it. Drag the grip on its right edge to resize it.
4. Turn `EditMode` back off.

Where you drop it is written back to the three layout settings, so it survives restarts and you can still fine-tune the numbers by hand.

## Compatibility

It reads the game's own pin list, so it works with vanilla pins on its own. Cartur's Map Pins is optional. With it installed, the compass shows that mod's pins in their map colours and drops chests it has marked as looted. The compass looks the mod up at runtime, and without it nothing breaks.

The mod patches one game method, `TombStone.UpdateDespawn`, to learn when a grave is emptied. If that patch fails, the compass still works and recovered graves keep their marker.

Client-side. The server does not need it and neither do the people you play with.

## Install

Use a mod manager (r2modman, Thunderstore or Gale) and it pulls in BepInEx for you. To install by hand, drop `CarturCompassAndClock.dll` into `BepInEx/plugins`.

---

*Free, and always will be. If it improved your game you can [tip me on Patreon](https://www.patreon.com/c/cartur).*

**[Discord](https://discord.gg/nd5RqpwNkz)**: bug reports, install help, and mod requests.
Bug reports get their own thread so nothing is lost in a chat scroll, and requests are voted on.

**More from Cartur:**
[HD Blood](https://thunderstore.io/c/valheim/p/Cartur/Carturs_HD_Blood/) ·
[Map Pins](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Map_Pins/) ·
[Safe Stamina](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Safe_Stamina/) ·
[Follow Command](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Follow_Command/) ·
[Flooring](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Flooring/) ·
[UI HUD](https://thunderstore.io/c/valheim/p/Cartur/Carturs_UI_HUD/) ·
[Waste Management](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Waste_Management/) ·
[Feeding Trough](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Feeding_Trough/) ·
[Combat Text](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Combat_Text/)
