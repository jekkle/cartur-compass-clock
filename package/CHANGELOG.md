# Changelog

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
