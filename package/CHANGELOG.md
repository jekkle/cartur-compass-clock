# Changelog

## 1.5.3

- **ClockTextSize setting.** Make the clock text bigger or smaller without touching the bar.
  The clock still sits on top of the frame at any size. Requested on Nexus.
- **Opacity setting.** Fade the compass and clock so they stand out less. Requested on Nexus.

## 1.5.2

- **Bronze frame.** The compass bar is redrawn in bronze knotwork to match Cartur's UI.
- **ShowClock setting.** Off hides the clock completely and leaves the compass. Requested on Discord.

## 1.5.1

- Finds Map Pins' looted-chest setting again: Map Pins renamed its config sections, and the compass now looks the setting up by key.
- Looted graves are cleared per world, so a grave looted in one world no longer hides one in another.

## 1.5.0

- **`General / ShowDay`.** Turn it off to show just the time above the compass, without the time
  of day and day number.

## 1.4.1

- **The clock no longer disappears after edit mode.** Any settings change - including the one
  written when you finish dragging the compass - rebuilt it with the clock switched off, and
  nothing switched it back on until a restart.

## 1.4.0

- **The compass hides during a cinematic.** Sleeping after a boss kill plays a full-screen
  video and the compass was drawn straight over it.
- **Grave markers no longer have to crowd the bar.** New DeathMarkers setting. Graves were
  exempt from the range test entirely, so every grave a character had ever left was drawn at
  any distance, and a long-lived character ended up with a bar full of skulls. All keeps that
  behaviour, InRange makes graves obey PinRange like every other pin - so the one you just made
  still shows while you walk back to it - and Off draws none. Your map is untouched either way.

## 1.3.1

- **Markers no longer freeze at the edge of the compass.** The sweep that retires a marker and the
  draw loop disagreed about how far is too far, so a pin in the gap between them was dropped by the
  draw without ever being hidden, and its marker stayed on screen.
## 1.3.0

- **Home is always on the compass, in gold.** The bed you last slept in shows at any
  distance, at a fixed size, and does not fade out - the whole point of a home marker is
  the walk back from somewhere you have never been. The name under the frame reads "Home".
- **Graves show in dark red, at any distance, over everything else.** A grave whose
  tombstone you have emptied stops being drawn, so only the ones still holding your gear
  stay on the bar. That lasts for the session; the pin itself is left alone on the map.
- **A crowded bar stays readable.** When several pins land on the same stretch of the
  compass, the nearest one keeps the spot and the ones behind it are hidden until the view
  thins out. Nothing is filtered by distance or by type, and home and graves are never the
  ones dropped.
- **Pins take their colour from Cartur's Map Pins.** A pin you have coloured on the map is
  drawn in that colour on the compass, ore tints included. Without that mod, or with a pin
  left at its default style, nothing changes.
- **The clock says what time of day it is, and names the day.** Three phases now - Morning
  06:00-12:00, Afternoon 12:00-18:00, Night 18:00-06:00 - and the day number is labelled, so
  it reads "Morning, Day 106 - 9:14 AM" instead of "Night 106". Dawn and Dusk are gone: they
  were narrow bands that told you nothing the clock beside them did not. Translated into the
  same eleven languages as the rest of the clock.
- **The name under the frame clears when you turn away.** It only names a pin within 12
  degrees of centre now, instead of anywhere in the visible arc.

## 1.2.1

- **The compass no longer eats mouse clicks.** Three of its parts - the centre tick, the
  clock and the name label under the frame - were set to catch clicks, which they have no
  use for, so anything of the game's sitting behind them could not be clicked. All three
  now let clicks straight through. Only the edit-mode box and its resize grip catch clicks,
  which is what they are for.

## 1.2.0

- **Emptied chests drop off the compass again.** If you also run Cartur's Map Pins,
  the compass hides chests you have already cleared out - it reads that mod's
  looted-chest icon to know which those are. Map Pins 1.3.0 renamed that setting,
  changed its type and moved where its icons live, so the compass stopped
  recognising them and looted chests came back. It now reads all three rather than
  assuming them, and asks Map Pins directly where its icons start, so a future icon
  set will not break it the same way.
- Links to Cartur's Flooring.

## 1.1.3

- Store page only - the plugin is unchanged from 1.1.2. Added in-game screenshots,
  a new icon taken from one of them, and a link to Cartur's Follow Command.

## 1.1.2

- Moved the links to my other mods to the top of the page.

## 1.1.1

- Added links to my other mods.

## 1.1.0

- Edit mode. Turn on `EditMode` and a box appears around the bar: drag the box to
  move the compass anywhere on screen, drag the grip on its right edge to resize it.
  Position and size are saved when you let go. The compass stays visible in menus
  while edit mode is on, so you have a free cursor to drag with.
- New `FrameOffsetX` setting for horizontal position. The bar could only be moved
  up and down before.
- The slim tick-marked bar is now the frame you actually get. 1.0.0 shipped the older
  wide Nordic frame by mistake - the store icon has shown the slim one all along.
- New defaults: a little wider, a little further down, horizontally centred. Existing
  configs are left exactly as they are - this only changes a fresh install.
- The clock is set in Valheim's own serif, Averia Serif Libre, instead of Arial.
- New `TwelveHourClock` setting, on by default: the clock reads `Day 42 - 1:05 PM`.
  Turn it off for `Day 42 - 13:05`.
- The clock names the time of day - **Dawn, Morning, Afternoon, Dusk, Night** - so the
  hour means something at a glance. Night keeps the game's own 18:00 to 06:00, the
  boundary its spawns and sleep rules run on, so the word still answers "is it safe out".
  Dusk is the two hours before dark, as a warning.
- **Pin names under the bar are readable again.** A chest read `$piece_chestwood`
  rather than its name. Pins store a raw localization token and the map translates it
  on the way to the label; the compass was printing the token straight through.

## 1.0.0

First release.

- Compass bar with cardinal and intercardinal headings and a tick every 15°.
- Map pins placed at their real bearing, scaling and fading with distance.
- Name and distance shown for the pin nearest the centre of the bar.
- Checked-off pins, emptied chests (with Cartur's Map Pins) and pins within
  8 metres are left off the bar.
- Hides with the HUD, the large map, the inventory and the game menu.
- In-game day and time above the bar.
