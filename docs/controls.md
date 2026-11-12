# Controls

Every key, checked on 2026-11-12 against `godot/App/GameInput.cs` (keys, mouse), `godot/App/GameUse.cs` (what E
reaches) and the K line in `godot/Ui/Hud.cs`. K in play shows the game's own list; `godot/SelfTest/SelfTest.cs`
drives most of these with real input. "FP" is first person; "Colony" is the map screen's Colony tab (B, P or O from
first person; a click on the Colony button). There is no pause key: the map and the menu pause, the Colony tab plays on.

## Keys

| Key | What it does | Where |
| --- | --- | --- |
| Mouse | Look | FP |
| W A S D, arrows | Walk the way you face (A, D strafe). In a car: W go, S brake and reverse, A D steer. Colony: walk in 8 ways | FP, Colony (when you play someone) |
| Shift | Run (not swimming, not on sights) | FP, Colony |
| Space | Jump; vault a broken window, barricade or yard fence just ahead (they stop you walking); hop one tile of water; a stroke when swimming (hold to keep stroking). At a parked car: walking into it goes over, standing still climbs onto it; on top, Space jumps down. All cost endurance | FP, Colony |
| C | Sneak on or off. Crouched, E at a parked pickup gets you under it (the dead that did not see you go lose you) | FP |
| Left click | Swing what you hold (bare hands punch); with a loaded gun fires where you look; at a parked car a blow that dents it (the car's alarm may go off) | FP |
| Right click | Use what the crosshair is on (same as E). With a gun in hand, held: aim down the sights (view narrows, spread halved, walk at 60%, no running) | FP |
| E | Use what the crosshair is on within 1.6 m (a drawer, crate, door, car, item on the floor), else the tile in front, else underfoot (forage on grass by day, sleep). In a car: get out. At a parked car: get in from any side. Behind a car with its boot open: shows the boot again | FP |
| U | Open or shut the boot of the car beside you (from behind it); the inventory screen opens on it | FP |
| I | Open or shut its bonnet (from in front) | FP |
| J (held) | Push a parked, empty car, from behind or in front (about 1 m/s, costs endurance) | FP |
| R | Work the tile in front: repair; with wood, barricade a door, board a window, reinforce, build a wall. In the inventory screen: turn the held thing | FP, inventory |
| X | Reload the gun in hand from your bag (a magazine, or a round at a time). In the inventory screen: split a stack | FP, inventory |
| Q | Take up the next weapon from the bag. In the inventory screen: jump to the previous grid | FP, inventory |
| 1-3 | That quick slot: take its thing in hand (the old one to the bag) or use it; the same number again puts it away. In the inventory screen over a thing: put it in that slot | FP, inventory |
| 0 | Empty your hands (weapon into the bag) | FP |
| G | Flashlight on or off; in a car, the headlights | FP |
| Z | The light switch of the room you are in, or past the door in front of you. In the inventory screen: arrange the grid | FP, inventory |
| L | Tell your people to loot the room where you look | FP |
| V, H | "Follow me", "hold here" | FP |
| T | Talk (a neighbour's person may ask you for an errand) | FP |
| Tab | Open or close the inventory screen (bag, what is beside you, the character sheet). In the build bar, Tab changes category | FP, Colony |
| M | Map screen: tabs Map (the ground round you), Manage (with the colony's controls), World, Back. M or Esc goes back | FP, Colony |
| B, P, O | Open the Colony tab; then B the build bar, P the survivor list, O the overview | FP, Colony |
| Esc | Close the inventory, else cancel a tool, else open the menu (Colony: back to first person) | everywhere |
| K | Show or hide the key list | everywhere |
| ` (backtick) | Debug panel (tick, sim and draw ms) | everywhere |
| N | Minimap | everywhere |
| + - (also keypad) | Game speed 1x, 2x, 4x | everywhere |
| Home | Recentre the Colony view | Colony |
| F | Play the selected survivor instead; it never lets go of the one you play (only dying does) | Colony |

## Mouse in the Colony tab

Left click selects (or paints with a build tool; drag for boxes); right click moves the selected survivor (or puts a
tool away); wheel zooms; middle or right drag pans. Build bar: 1-0 pick tools (in the Colony tab), Tab, E and Q change category, Shift
for instant. P: fold or open the list; Shift-click a priority steps it back. In the full-screen map: wheel zoom, drag
pan, click pins a note, right-click removes a pin, double-click looks there. 1x/2x/4x buttons beside the clock.
With you playing in the Colony tab, a click on a tile fires a loaded gun at it, and a swing at glass in front breaks it.

## The inventory screen (Tab)

Grids of cells. Left: the character sheet (who you are, health by body part, what is wrong, needs, mood, what you
wear, skills; it only reads and scrolls with the wheel). Then you: In hand, Body, Feet, Tool, the three quick slots (1-3), the
bag (8 x 6, 12 kg at most) and the pockets of a coat or vest. Right: the container you are at (crate, shelf, fridge
8 x 8; a car's boot 10 x 6, a truck's 12 x 10; a body on the floor), its pockets, and the ground beside you.

| Input | What it does |
| --- | --- |
| Drag | Move a thing: green where it fits, amber where it swaps, red where it cannot go (reason shown). Onto a slot to wear or wield it, onto a quick slot to assign, anywhere off the panels to drop |
| R while holding | Turn a quarter turn. Esc or right-click lets go |
| Shift-click, Ctrl-click | Move the stack across, move one |
| Right-click | Menu: eat, use, wear, wield, take, put in, open its pockets (search a body), split, turn, drop, quick slot |
| Arrows | Move the gold cursor (off a grid's edge into the next); Q and E jump between grids |
| Enter | Use what is under the cursor |
| Space | Pick up, put down (arrows carry it) |
| F, Shift+F | Move it across the grids (one) |
| G, Shift+G | Drop one (the stack) |
| X | Split a stack in half |
| Z | Arrange the grid, biggest first |
| Buttons | Arrange, Take everything, Stash everything |

Picking up something usable of a kind no quick slot has fills the first empty slot; when a slot's thing is used up it
passes to another of its kind in the bag.

## In a car

The speedometer (km/h), fuel gauge, temperature (climbs as the car is damaged) and lamps (FUEL under 15%, ENGINE under
half health, OIL and BATT when the engine has stopped, LIGHTS) are on the dashboard: look down. The view turns with the car;
the mouse looks round the cabin. Getting in or out takes a moment (walking off stops it).

## In water

A tile beside the bank is shallow: you wade at 60% pace and can still run, swing and use. Further out you swim: 40%
pace, no running, hands busy, endurance draining. Space is a stroke. Winded out of your depth, the water hurts you.
The dead, animals and colonists keep to land.

## Saves

Three slots and the dawn autosave in `%APPDATA%\Godot\app_userdata\LoonieRoo\saves`. "File…" and "Report a bug" write to
Documents/LoonieRoo (a bug report replays the game from where it started).

## Where the K line is out of step with the code (2026-11-12)

- (Checked: the K line's "1-3" is right, `Inventory.QUICK_SLOTS` is 3 since 2026-11-12; the comments at the top of godot/Ui/Inventory.cs and its hover line still say 1-9, and its key handler ignores 4-9.)
- It leaves out the keys that work in first person but are not listed: ` (debug panel), N (minimap), + and - (speed).
- It lists no Colony-tab keys beyond "M or Esc", "Tab" and "F" (no B, P, O, Home, build-bar keys).
