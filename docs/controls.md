# Controls

The keys as of 2026-11-09 (moved from NOTES.md "Start here" on 2026-11-10). The game's own key list is K in play;
the self-test (godot/SelfTest.cs) drives most of these with real input.

## First person (playing a survivor, since 2026-11-08)

You see through your survivor's eyes. The mouse looks; WASD walks the way you face (strafing with A and D); Shift
runs; Space jumps (vaults a broken window or a barricade just ahead, hops a tile of water); click swings; right-click
or E uses what you look at on the ground beside you (else what's in front). Tab opens the inventory (bag, what's
beside you, your card; the mouse is let go). M opens the map screen (tabs: Map, Colony, Back); B, P or O open the
Colony tab. Esc opens the menu. The map and the menu pause; the Colony tab plays on. There is no pause key.

**The character sheet** is the left column of the Tab screen: who you are, your health (a bar and a word: fine, hurt,
badly hurt, dying), the body by part (an outline tinted by each part's state; hover it for the details), what is wrong
with you worst first with what would help, then body heat, endurance, wetness and panic, needs, mood and the thoughts
behind it, what you wear and wield, skills and your story. It only reads: nothing on it changes the game. It scrolls
(wheel) when it is taller than the window, and follows the survivor you control. The same sheet as text:
`CharacterSheet.textOfControlled(state)` (cs/Sim/Systems/CharacterSheet.cs).

The direct-control keys below apply as before (L loots the room where you look). In the Colony tab you stand still
and the colony's controls apply; M or Esc goes back to first person; F on someone selected plays them instead.
G is gone.

## The Colony tab (2D top-down)

- **Colony:** left click selects, right click moves, B build bar (1-0 tools, Tab/E/Q categories, Shift for
  instant), P the survivor list (fold it, or open it to every need and job priority; Shift-click a priority to step it
  back), O overview, M map (full screen: wheel zoom, drag pan, click to pin a note, right-click a pin to remove,
  double-click to look there), N minimap, 1x/2x/4x buttons (or + and -), `` ` `` debug panel (with tick/sim/draw
  ms), Esc cancels then opens the menu.
- **Direct control** (F on the selected survivor, F again to let go) starts in survivor mode, with the colony's
  management put away; B, P or O bring it out (build, priorities, overview, neighbour and visitor panels).
- WASD/arrows walk (8 ways), Shift run, Space jump (vault, hop), C sneak, the mouse aims, click swing or shoot (a
  swing at glass in front breaks it).
- Right-click or E use: pick up, wear, eat, drink, sleep, treat yourself, search, smash a window, get in or out of a
  car (a moment each way: walking off stops it), fill a car from a fuel can. U opens or shuts the boot of the car
  beside you, I its bonnet. E underfoot forages on grass by day or sleeps.
- In a car: W go, S brake and reverse, A D steer.
- 1-9 use from the bag (stash into a crate or car beside you from the bag panel). R works the tile in front (repair;
  with wood: barricade a door or board a window, reinforce, build a wall).
- T talk (neighbours' people may ask you for an errand), V "follow me", H "hold here", L "loot that room" (under the
  mouse), K the full key list.

## Saves

Three slots and the dawn autosave in Godot's user folder (`%APPDATA%\Godot\app_userdata\LoonieRoo\saves`). "File…"
and "Report a bug" write to Documents/LoonieRoo (a bug report replays the game from where it started).
