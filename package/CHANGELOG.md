# Changelog

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
