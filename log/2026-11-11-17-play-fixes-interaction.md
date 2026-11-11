- **2026-11-11, the playtest's interaction fixes** (branch claude/fix-interact-8461c3). The player's first hand playtest
  reported six things about E and Space at cars, drawers, fences and windows. Each was reproduced first, then fixed with a
  sim test or a self-test step (new: `godot/SelfTest/SelfTestInteract.cs`, 16 checks with real keys; `SELFTEST_ONLY=interact`
  runs just those).
  - **1. "Getting into cars with E doesn't work."** First a sim test (`CarUseTest`, "from any side"): walk up to a parked
    sedan and a pickup from 16 directions at 11 headings (314 walk-ups) until the body (or, at the ends and on angled
    cars, its parked tiles) stops you, look at its middle, `use` the tile the game's E picked: every one got in. So the
    sim's reach was not it. The self-test with real input found it: **E with W still held** (walking up to the car) started
    getting in and the held W cancelled it on the next tick, since any steering cancelled a busy action. Now a car action
    (getIn, the boot, the bonnet) goes on while steering doesn't move you more than `STILL_AT_CAR` (12 SUB) a tick: pushed
    against the car you stay put and it goes on; walking off along it stops it (`ControlMod.updateControlled`; sim tests
    "W still held against the car..." and "W held at the boot"). Self-test: E gets you in from the left with W held, the
    right, behind, in front and both corners.
  - **2. "Interacting with crates or drawers doesn't work."** The self-test found **E standing over a drawer's tile**
    (furniture doesn't block walking, so you end up on it) did nothing: E never tried the container under you, and the old
    pick took the ground point under the crosshair (past a raised drawer, often out of reach) or else the tile ahead by
    yaw. E is now `GameUse.cs`: the look is followed from the eye in 4 cm steps for USE_REACH (1.6 m along the ground),
    every tile it passes over is tried nearest first (the inventory screen on a searched container, else the sim's `use`;
    a refused use changes nothing and isn't logged), stopping at the ground, a car's body under its roof, an edge or a
    wall tile; then the tile in front, then the one under you (the drawer you stand over opens there; bare ground still
    forages or sleeps). Self-test: a searched dresser opens looked at straight on, from close up, stood over, and from a
    tile to the side; cloth on the floor is taken. Textures make no difference to any of it (the pick reads the sim).
  - **3. The boot and the bonnet.** U behind a car opens the boot and the inventory screen opens on what's in it (a tin
    stashed first); E behind a car with its boot open now opens the boot (before, it started getting in); I in front lifts
    the bonnet. The K help and docs/controls.md say where to stand.
  - **4. Wheels the wrong way.** Pictures of a sedan from the side and from above (`--car=152,92,0 --eye=...`): the
    wheels showed their tread from the side and faced forward. `wheel()` in art/scripts/build_furniture.py laid every
    axle along X, and cars, the pickup and the tractor run along X: the axle is now along Y (the wheelie bin's pair, side
    by side along X, keeps "x"). Rebuilt (`experiments/textures/models/rebuild.sh furniture`), looked again: round wheels
    from the side on the sedan and the pickup, hubs outward from above.
  - **5. "I can walk through fences."** Yard fences (Lots.cs, 35% of back yards) are `EDGE_BARRICADE` edges, and
    `Pathfinding.crossable` let the "free" mover (only you) through barricades. The quarantine fence is wall tiles,
    solid already; farms have no fence of their own; no fence is decor only. Now barricade edges stop you, and Space
    vaults them as before. Colonists still step over (their paths are "survivor"); the dead stay out.
  - **6. Broken windows need Space.** "free" no longer crosses `EDGE_WINDOW_BROKEN`; the dead and animals still climb
    through. Straddling one on an old save, `ControlMod.walkMover` walks you as a colonist or the dead until clear (sim
    test). The vault plays in first person: the self-test sees the eye go up 0.3-0.55 m and the body 0.5 m, and
    `LEAP_SHOT` pictures from the side (strafing with D, the eye beside the window) show the tucked vault clip over the
    sill and the planks. Sim tests: "walking, a broken window or a fence stops you at it; Space vaults you over",
    WindowsTest's walking test, EdgesTest's barricade test.
  - **The scripted solo player** paths over barricade edges ("survivor" paths), so it now jumps when a fence is the
    next edge and the vault is in reach (SoloHarness). Solo balance, before and after: seeds 1 and 2 identical line for
    line; seed 3 diverges chaotically from tick 240 (a zombie a tile over) and then dies on day 1 stuck on a tree trunk
    at (21, 75) for 3000 ticks (the scripted player's old trouble with trunks, no fence or window there), where before it
    lived to day 7. Worth a look when the harness is next tuned; not a game problem.
  - **Decided.** docs/decisions.md "Movement and pathfinding": the two entries (windows and fences stop you; a car
    action goes on while you push against the car).
  - **Left.** Furniture still doesn't block walking (you can stand inside a dresser); the old whole-tile "barricade"
    tile is still walkable for you (only edges changed); angled parked cars' tiles stick out past their bodies, so you
    can stop up to ~0.9 m short of one (E still reaches: 290 SUB). Not played by a human yet. Pictures: the side-session
    notes folder, play-fixes-interaction-pics/. Check: see the commit (green `./check.sh --all`).
