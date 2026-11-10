- **2026-11-10, the inventory as grids** (branch claude/grid-inventory-4b9037). The player asked for spatial inventory
  management in the style of extraction shooters and survival-horror cases: every container a grid of cells, every item
  a footprint. Built in this game's own style and words (plain labelled tiles: the game has 3D models, no icons).

  **Sim** (cs/Sim/Systems/InventoryGrid.cs, new; Inventory.cs). Sizes are data beside the weights
  (`Inventory.ITEM_SIZE`: w, h, stack; a kind not listed is 1x1, one to a cell, so the flashlight needs one line;
  `NAME_SIZE` by name for weapons):

  | kind | cells | stack | | kind | cells | stack |
  |---|---|---|---|---|---|---|
  | wood (planks) | 2x4 | 1 | | gun | 2x2 | 1 |
  | food (a tin), meal | 1x1 | 1 | | ammo | 1x1 | 10 |
  | bandage, medicine | 1x1 | 5 | | book | 1x2 | 1 |
  | cloth, sample | 1x1 | 5 | | axe | 2x4 | 1 |
  | weapon | 1x3 (knives 1x2, machete, crowbar, tyre iron 1x3, bat 1x4) | 1 | | hammer | 1x3 | 1 |
  | coat, armour | 2x3 | 1 | | medkit, scrap, fuel can | 2x2 | 1 |
  | boots | 2x2 | 1 | | cart 4x3, corpse 5x2 | never in a bag | |

  Grids: the bag 8x6 (always there; no backpack item: one would mean reweighting every loot table, so the bag stays one
  fixed grid), a coat's pockets 2x2 and a vest's pouches 3x1 (worn or carried: they're grids of yours), a crate (every
  container furniture: counter, shelf, fridge...) 8x8, a car's boot 10x6, a truck's 12x10, a body 3x3 (nothing is
  generated in one: you can put things there). The ground isn't a grid in the sim (items stay one per tile); the screen
  draws the ground beside you as one (`packLoose`). Where things are: carried items stay in `survivor.pack` (so every
  old "find food in the bag" still works) with `Item.grid` naming the container item they're in (null: the bag); in any
  other grid they're `inside` it; `gx`, `gy`, `turned` place them. A container item keeps its contents wherever it
  goes (`follow`: worn, bagged, stashed, dropped, picked up with E); one level deep, so nothing goes inside itself;
  removed (worn through) it spills them (`spill`, from Items.removeItem). Layout is pure: things without a cell (an old
  save, a search's finds) are laid out by first-fit in item order, under the grid if it's overstuffed (it shows extra
  rows until emptied); every move settles the grids it touches; a refused command changes nothing (it isn't logged).
  First-fit: a stack of its kind with room, else row by row, left to right, as it lies, then turned. take, takeAll,
  stash, stashAll, unequip, the start kit, E's pick-up and the scripted solo player all go through it.
  Weight: PACK_MAX kept at 12 kg over everything in your grids; a crate's or boot's load counts what's in the pockets
  of what's in it. New commands: `moveItem` (itemId, containerId: a grid or "ground", x, y, turned, count), `swapItems`
  (itemId, id), `splitStack` (itemId, count: half by default), `quickMove` (itemId, containerId: none for yours,
  count), `arrange` (containerId: none for the bag; biggest first, refused if it wouldn't hold everything), `setQuick`
  (slot 0-8, itemId: none clears). Reasons a UI can show: "something's in the way", "it doesn't fit there", "that stack
  is full", "no room in your bag", "your bag is too full", "it won't fit: too heavy", "it can't go inside itself", "a
  parka won't go inside another", "out of reach", "a cart won't go in a bag", "nothing to split", "no room to split it",
  "they don't fit each other's places", "it won't pack any tighter".
  Quick slots: `survivor.quick`, nine item ids ("" empty), set by `setQuick` or filled on pick-up with the first of a
  usable kind (food, remedies, weapons, tools) no slot has; a used-up thing's slot passes to another of its kind
  (`forgetQuick`); keys 1-9 use `Inventory.quickItem` (Game.cs, one line). An old save (quick null) keeps the bag's
  first nine until a slot is first filled.

  **Saved fields** (all optional, left out when null; no schema bump, still 77): Item `gx`, `gy`, `turned`, `grid`;
  Survivor `quick`; Command `turned`, `slot`. An old save loads with nothing placed and lays out by first-fit, the same
  on every load (tested by saving with every place stripped and loading twice).

  **Screen** (godot/Ui/Inventory.cs, rewritten): a column kept free on the left for the character sheet (marked
  `// CHARACTER SHEET COLUMN`, `InventoryScreen.SheetColumn`, 320 px); your column (In hand, Body, Feet, Tool tiles; the
  quick slots; carried weight; the bag and your pockets with their free cells; Arrange); the container's column (its
  grid, its pockets, Take/Stash everything, Arrange) over the ground beside you. A custom drag (InventoryScreen._Input,
  not Godot's drag and drop) with a ghost: green fits, amber swaps, red with the reason; R turns, Esc lets go; slots
  take a drop to wear or wield, quick tiles to assign; off the panels drops. Shift-click: the stack across; ctrl-click:
  one. Right-click menu (use, take off, open its pockets / search the body, take or put in, split, turn, drop, drop all,
  quick slot). Keys: arrows, Q/E, Enter, Space (pick up and carry), F, G, X, R, Z, 1-9 (docs/controls.md).

  **Checked:** 12 new sim tests (InventoryGridTest: footprints, turning, overlap and edge refusals; first-fit turning
  and the same twice; refusals; stacks, split, merge, partial merge; swap, quick-move, arrange; nesting worn, bagged,
  stashed, a coat can't go in itself or another; dropping a coat drops its pockets with it and E wears it with them; a
  coat worn through leaves its pockets in the bag; death drops everything as before; an old save lays out the same on
  two loads and the first move keeps it; a search places its finds; every move replays from the log; quick slots).
  One old test changed: the crate-weight test filled the crate with 8 plank bundles, which no longer fit by space; it
  uses guns now and still tests the weight limit. 739 sim tests green. Self-test: the inventory steps rewritten (14
  checks: E opens the shelf, shift-click, ctrl-click, a drag to a bag cell, R while dragging, Take everything, a drag
  onto Body and from Body to a cell, a drag out onto the world, names, F on the cursor, Esc, the Colony tab twice; with
  `--invshot` a 15th, Esc during a drag), 79 in all. Harness: the solo player's seeds 1 and 2 are identical before and after (docs/harness.md).
  Pictures (`./shot.sh --selftest --invshot=<abs>/inv.png --shot-width=960`): .check/inv.png (a full bag, 12.0/12.0
  kg, with a bandage stack in the coat's pockets, beside a stocked shelf), .check/inv-drag.png (holding a hatchet over
  the bag: red, "something's in the way"), .check/inv-colony.png. Two footstep-audio self-test checks failed once and
  passed on the next two runs (flaky, unrelated).

  **Left / options for the player:** identifying each find takes time, as some games in this style do (not built);
  a backpack item (would need the loot tables reweighted) or more sizes of bag; icons rendered from the item models;
  bodies hold nothing generated (searching a dead survivor could turn up their kit); colonists still haul one item at a
  time and never use grids; a coat a colonist wears keeps its pockets' contents hidden until it's dropped; the screen
  not yet played by a human (tile text is small at 30 px cells).
