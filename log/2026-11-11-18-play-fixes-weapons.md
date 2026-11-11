# 2026-11-11 18: play fixes, hands and weapons

The player's first hand-played pass reported four things about hands and weapons in first person. One of six
parallel sessions (this one: `claude/fix-weapons-e8a2ba`). Pictures in `experiments/play-fixes/weapons/`.

## 1. "Punching has no first person animation"

**Reproduced:** `./shot.sh --seed=3 --arm=none --swingat=0.2` (bare hands, a swing held) drew nothing at all:
Held.cs only drew a hand round an item's grip. **Fixed:** with nothing in your right hand and nothing carried, a
swing draws a fist (the hand model alone) up from below the view, drawn back, driven to the middle of the view and
pulled back down, over the swing's 6 ticks; nothing is drawn at rest. `--arm=none` (Staging.arm) empties your hand
for pictures. Pictures: `punch-wind.png`, `punch-strike.png`, `selftest-punch.png`. Self-test: "With empty hands a
click punches: the fist comes through on the swing" (the fist moved 0.47 m).

Left: with a hammer or an axe in the tool slot and the weapon hand empty, the sim hits with the tool
(Weapons.meleeSpec) but the view still punches with the right fist.

## 2. "I still see a shooting animation even when out of ammo"

**Reproduced:** `--arm=pistol:0 --swingat=0.2` drew the kick and a full muzzle flash
(`before-empty-pistol-kick-and-flash.png`): Held.cs played the kick on any `swungTick`, and the sim sets that for a
blow too. **What the sim does:** an empty gun swings as a club (`Weapons.BUTT`, ControlMod.melee): a real blow, with
the swing's whoosh (Audio/Yours.cs already plays it, since the gun's wear doesn't drop). So the view now shows that:
**the pistol-whip** (raised, brought down ahead and to the left), a rifle or shotgun's butt driven forward, no kick,
no flash. A dry click would have shown something the sim doesn't do. The sim gained `control.shotTick` (the tick of
your last shot); a swing on another tick was a blow. Pictures: `empty-pistol-whip-wind.png`,
`empty-pistol-whip-strike.png`. Sim test "hands > a gun with nothing in it swings as a club"; self-test "An empty
pistol's click is a blow, drawn as a pistol-whip".

## 3. Holstering and a hotbar

**Reproduced:** with a weapon in hand there was no key that emptied it (Q only cycles to the next weapon), and the
weapon in hand vanished from its quick slot (Inventory.quickItem only looks in the bag), so pressing its number did
nothing. **Fixed:**
- Sim: the `quick` command (slot 0-8, Inventory.useQuick): that slot's thing in hand from the bag or used, as
  `useItem` did; if it is already in your hand (`quickInHand`), it goes into the bag. `holster` (key 0) puts the
  weapon in hand away. A full bag refuses with "your bag is full: the crowbar stays in your hand" (a toast); a swap
  whose old weapon won't fit drops it at your feet as before, but the note now says so and is toasted.
- Game: keys 1-9 send `quick`, 0 `holster`; the hotbar (godot/Ui/Hotbar.cs) along the bottom in first person: nine
  slots and "0 hands", the one in hand lit gold. The inventory screen's quick slots show the thing in hand too.
- Dragging onto a quick slot already worked (InventoryScreen sends `setQuick`); it now has a self-test step.
Pictures: `selftest-hotbar.png`. Sim tests "hands > a quick slot takes its weapon in hand...", "...swaps weapons...",
"hands > holster ... with the bag full it says so"; self-test steps for 0, a slot's key twice, a swap, and the drag.

## 4. "pistol cannot be iron sighted"

**Reproduced:** right-click with a pistol in hand ran "use" (docs/controls.md); nothing aimed. **Binding:** the
right mouse *held* with a gun in hand aims down the sights; without a gun a right-click is still "use", and E
always is, so nothing loses its use key. **Sim:** the sim does have an accuracy number (Shooting.spreadOf, the
weapon's spread in thousandths), so the sights go through a command: `sights` (on/off, sent when the button
changes) sets `control.sights`; ControlMod.sightsUp (asked for, a gun in hand, on foot) halves the weapon's own and
the moving spread (Shooting.SIGHTS_SPREAD_PCT 50: a still pistol 0.035 to 0.0175), walks at 60%
(ControlMod.SIGHTS_PCT) and stops running (the pose intent too). Panic and winded spread are not halved. **View:**
the gun eases to the eye line in 0.18 s (the pistol's rear sight on the crosshair, the rifle's scope centred), the
field of view narrows from 60 to 47 degrees, the walking bob drops to a fifth. `--hold=aim` holds it for a picture.
Pictures: `selftest-sights.png`, `sights-rifle.png`. Sim test "hands > down the sights..."; self-test "The right
mouse held with a pistol aims down the sights" and "Letting go lowers it".

## Checks

`./check.sh --all` green. One self-test run had the two footstep-sound checks fail ("Walking, your steps are heard")
and the next passed all: flaky, not these changes (they don't touch steps or sound).

## Left

- The tool-slot blow (above) is drawn as a punch.
- No dry-fire click sound: the sim never dry-fires, so none was added.
- The sights don't change the crosshair (still the dot) or the sway of a held breath.
