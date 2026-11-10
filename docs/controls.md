# Controls

The keys as of 2026-11-09 (moved from NOTES.md "Start here" on 2026-11-10). The game's own key list is K in play;
the self-test (godot/SelfTest/SelfTest.cs) drives most of these with real input.

## First person (playing a survivor, since 2026-11-08)

You see through your survivor's eyes. The mouse looks; WASD walks the way you face (strafing with A and D); Shift
runs; Space jumps (vaults a broken window or a barricade just ahead, hops a tile of water); click swings; right-click
or E uses what you look at on the ground beside you (else what's in front).

**Space at a parked car** (within a swing's reach ahead, on the nearest of the four sides of your way). Walking into it
(W held), Space goes **over** it to the far side: a slide over the bonnet or the boot, a longer scramble over the roof
(through the cabin, side to side or nose to tail), over a pickup's bed behind its cab. Standing still facing it, Space
climbs **onto** it; walking into one whose far side has no room (a wall, another car) climbs onto it too. All of these
cost more endurance than a vault, and winded you can do none of them. **On top** you walk about within the car (the
eye is higher: 1.28 m more on a sedan's roof, 1.75 m on a pickup's cab); walking off an edge steps you down where
there is room below, else you stop at the edge; Space jumps down on your way, or where you stand if there is no room
below. You can get into the car from its roof. The dead reach you up there as on the ground: from beside the car. Tab opens the inventory (bag, what's
beside you, your card; the mouse is let go). M opens the map screen (tabs: Map, Colony, Back); B, P or O open the
Colony tab. Esc opens the menu. The map and the menu pause; the Colony tab plays on. There is no pause key.

**The character sheet** is the left column of the Tab screen: who you are, your health (a bar and a word: fine, hurt,
badly hurt, dying), the body by part (an outline tinted by each part's state; hover it for the details), what is wrong
with you worst first with what would help, then body heat, endurance, wetness and panic, needs, mood and the thoughts
behind it, what you wear and wield, skills and your story. It only reads: nothing on it changes the game. It scrolls
(wheel) when it is taller than the window, and follows the survivor you control. The same sheet as text:
`CharacterSheet.textOfControlled(state)` (cs/Sim/Survivors/CharacterSheet.cs).

**Weapons** (2026-11-10; the table of every weapon: cs/Sim/Combat/WeaponTable.cs, how shots fly: Shooting.cs).
Click swings what is in your hand, or, with a loaded gun, fires it exactly where you look (the `fire` command: a
direction). **X** reloads the gun in hand from your bag (the whole magazine for a pistol, a round at a time for a
revolver, shotgun or rifle; shooting or walking off stops it). **Q** takes up the next weapon from the bag, the one
in hand going into the bag in its place (or put a weapon in hand from the Tab screen as before). With a gun in hand
the bottom of the screen reads its rounds, the bag's rounds and what to do ("X: reload", "out of ammunition"); the
empty gun is a club. Two-handed weapons (sledgehammer, spear, axe, shotgun, rifle, crossbow) need free hands: not
while you carry something in them or push a cart. An axe or a hammer in the tool slot is what you hit with when your
weapon hand is empty. A survivor of yours in the line holds your fire.

The direct-control keys below apply as before (L loots the room where you look). In the Colony tab you stand still
and the colony's controls apply; M or Esc goes back to first person; F on someone selected plays them instead.
G is gone.

## The inventory screen (Tab)

Grids of cells (since 2026-11-10): on the left a column kept for the character sheet; then you (In hand, Body,
Feet, Tool; the quick slots 1-9; the bag, 8 x 6, and the pockets of a coat or vest you wear or carry; the weight you
carry, 12 kg at most); on the right the container you're at (a crate, shelf or fridge 8 x 8, a car's boot 10 x 6, a
truck's 12 x 10, a coat or body on the floor), the pockets of what's in it, and the ground beside you.

- **Mouse:** drag a thing to a cell; the ghost is green where it fits, amber where it would swap with what's there,
  red (with the reason under the panels) where it can't go. R turns it a quarter turn while you hold it; Esc or a
  right-click lets go. Drop it on a slot to wear or wield it, on a quick slot to put it there, on the ground grid or
  anywhere off the panels to drop it. Shift-click moves the stack across (yours to the open container; the
  container's or the ground's into your grids; what you wear off into the bag); ctrl-click moves one.
  Right-click: eat / use / wear / wield / hold, take or put in, open its pockets (search a body), split, turn, drop
  (one or all), quick slot.
- **Keys:** the arrows move a gold cursor (off a grid's edge into the next), Q and E jump between grids. Enter uses
  what's under it; Space picks it up and puts it down (the arrows carry it, R turns it, Esc lets go); F moves it
  across (Shift+F: one); G drops one (Shift+G: the stack); X splits a stack in half; R turns it in place; Z arranges
  the grid (biggest first); 1-9 put it in that quick slot. Buttons: Arrange, Take everything, Stash everything.
- **Quick slots:** nine assignable slots. Picking up something usable (food, a remedy, a weapon, a tool) of a kind no
  slot has fills the first empty one; when the thing in a slot is used up, the slot passes to another of its kind
  in your bag. A save from before the grids keeps the bag's first nine until a slot is first filled.

## The Colony tab (2D top-down)

- **Colony:** left click selects, right click moves, B build bar (1-0 tools, Tab/E/Q categories, Shift for
  instant), P the survivor list (fold it, or open it to every need and job priority; Shift-click a priority to step it
  back), O overview, M map (full screen: wheel zoom, drag pan, click to pin a note, right-click a pin to remove,
  double-click to look there), N minimap, 1x/2x/4x buttons (or + and -), `` ` `` debug panel (with tick/sim/draw
  ms), Esc cancels then opens the menu.
- **Direct control** (F on the selected survivor, F again to let go) starts in survivor mode, with the colony's
  management put away; B, P or O bring it out (build, priorities, overview, neighbour and visitor panels).
- WASD/arrows walk (8 ways), Shift run, Space jump (vault, hop; over or onto a parked car), C sneak, the mouse aims, click swing or shoot (a
  swing at glass in front breaks it).
- Right-click or E use: pick up, wear, eat, drink, sleep, treat yourself, search, smash a window, get in or out of a
  car (a moment each way: walking off stops it), fill a car from a fuel can. U opens or shuts the boot of the car
  beside you, I its bonnet. E underfoot forages on grass by day or sleeps.
- In a car: W go, S brake and reverse, A D steer. You sit in the driver's seat and the view turns with the car; the
  mouse looks round the cabin and out of it. Look down for the dashboard: the speedometer (km/h), the fuel gauge,
  the temperature (it climbs as the car is damaged), and the lamps (FUEL under 15%, ENGINE under half health, OIL
  and BATT when the engine has stopped, LIGHTS for the headlights).
- 1-9 use the quick slots (assigned on the inventory screen; see below). R works the tile in front (repair;
  with wood: barricade a door or board a window, reinforce, build a wall). X reloads, Q switches weapon (see
  Weapons above); in the Colony tab a click on a tile fires a loaded gun at it.
- T talk (neighbours' people may ask you for an errand), V "follow me", H "hold here", L "loot that room" (under the
  mouse), K the full key list.
- Lights (2026-11-10): G switches your flashlight on or off (one in your bag; using it from the bag does the same); in
  a car, G is its headlights. Z flips the light switch of the room you're in, or of the room past the door in front
  of you (the HUD says "Z: light on" or "light off" in a room). A room's light needs power: the mains, or a generator
  in reach.

## Saves

Three slots and the dawn autosave in Godot's user folder (`%APPDATA%\Godot\app_userdata\LoonieRoo\saves`). "File…"
and "Report a bug" write to Documents/LoonieRoo (a bug report replays the game from where it started).
