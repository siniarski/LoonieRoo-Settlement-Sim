// Consistency checks across survivors, jobs, items, and blueprints.
// Returns human-readable problems; empty means healthy. Used by tests after
// every tick and available to the debug panel. Read-only.

import { isPassable } from './pathfinding';
import { moverOf } from './zombies';
import { everyone } from './expeditions';
import { STRUCTURE_HEALTH } from './breach';
import { getTile, inBounds, isWalkable } from './grid';
import { growTicks } from './farming';
import { SATIETY_MAX } from './food';
import { HEALTH_MAX } from './health';
import { REST_MAX } from './rest';
import { labelAt } from './rooms';
import { SLOT_OF, isGarment } from './clothing';
import { HYDRATION_MAX } from './water';
import type { SimState } from './state';

export function checkInvariants(state: SimState): string[] {
  const problems: string[] = [];
  const { grid } = state;
  // Expedition members are away, but their ids (beds, opinions, weapons) are still in use.
  const survivors = new Map(everyone(state).map((s) => [s.id, s]));
  const jobs = new Map(state.jobs.map((j) => [j.id, j]));
  const items = new Map(state.items.map((i) => [i.id, i]));

  for (const s of state.survivors) {
    if (!isWalkable(grid, s.pos.x, s.pos.y)) problems.push(`${s.name} stands on unwalkable (${s.pos.x}, ${s.pos.y})`);
    if (s.jobId) {
      const job = jobs.get(s.jobId);
      if (!job) problems.push(`${s.name} holds missing job ${s.jobId}`);
      else if (job.reservedBy !== s.id) problems.push(`${s.name} holds ${job.id} reserved by ${job.reservedBy}`);
    }
    if (s.carrying) {
      const item = items.get(s.carrying);
      if (!item) problems.push(`${s.name} carries missing item ${s.carrying}`);
      else if (item.carriedBy !== s.id) problems.push(`${s.name} carries ${item.id} but it says carriedBy ${item.carriedBy}`);
      if (!s.jobId && s.meal?.itemId !== s.carrying) problems.push(`${s.name} carries ${s.carrying} with no job or meal`);
    }
  }

  const perBlueprint = new Map<string, number>();
  for (const job of state.jobs) {
    if (job.reservedBy) {
      const s = survivors.get(job.reservedBy);
      if (!s) problems.push(`${job.id} reserved by missing survivor ${job.reservedBy}`);
      else if (s.jobId !== job.id) problems.push(`${job.id} reserved by ${s.name}, who is on ${s.jobId}`);
    }
    if (job.blueprintId) perBlueprint.set(job.blueprintId, (perBlueprint.get(job.blueprintId) ?? 0) + 1);
  }
  for (const [bp, n] of perBlueprint) if (n > 1) problems.push(`blueprint ${bp} has ${n} jobs`);

  const occupied = new Set<string>();
  for (const item of state.items) {
    if ((item.pos === null) === (item.carriedBy === null)) {
      problems.push(`${item.id} must be either on the ground or carried`);
    }
    if (item.pos) {
      const key = `${item.pos.x},${item.pos.y}`;
      if (occupied.has(key)) problems.push(`two items on (${key})`);
      occupied.add(key);
      if (!isWalkable(grid, item.pos.x, item.pos.y)) problems.push(`${item.id} inside a wall at (${key})`);
    }
    const bearer = item.carriedBy ? survivors.get(item.carriedBy) : undefined;
    if (
      item.carriedBy &&
      bearer?.carrying !== item.id &&
      bearer?.weapon !== item.id &&
      bearer?.body !== item.id &&
      bearer?.feet !== item.id &&
      bearer?.cart !== item.id &&
      bearer?.tool !== item.id &&
      !bearer?.cartLoad.includes(item.id)
    ) {
      problems.push(`${item.id} says carriedBy ${item.carriedBy}, who isn't carrying it`);
    }
    if (item.reservedBy) {
      const holder = survivors.get(item.reservedBy);
      const job = holder?.jobId ? jobs.get(holder.jobId) : undefined;
      const using =
        holder?.meal?.itemId === item.id || holder?.treatment?.itemId === item.id || holder?.arming === item.id || holder?.dressing === item.id || holder?.reading?.itemId === item.id;
      if (job?.itemId !== item.id && !using) {
        problems.push(`${item.id} reserved by ${item.reservedBy} without a matching haul job, meal, treatment, or arming`);
      }
    }
  }

  const destinations = new Set<string>();
  for (const job of state.jobs) {
    if (!job.dest || !job.reservedBy) continue;
    const key = `${job.dest.x},${job.dest.y}`;
    if (destinations.has(key)) problems.push(`two haulers headed for storage tile (${key})`);
    destinations.add(key);
  }

  const furnitureTiles = new Set<string>();
  for (const f of state.furniture) {
    const key = `${f.x},${f.y}`;
    if (furnitureTiles.has(key)) problems.push(`two pieces of furniture on (${key})`);
    furnitureTiles.add(key);
    if (getTile(grid, f.x, f.y) !== 'floor') problems.push(`${f.kind} ${f.id} on non-floor (${key})`);
    if (occupied.has(key)) problems.push(`item on ${f.kind} at (${key})`);
  }

  for (const s of state.survivors) {
    if (s.rest < 0 || s.rest > REST_MAX || !Number.isInteger(s.rest)) problems.push(`${s.name} has rest ${s.rest}`);
    if (s.satiety < 0 || s.satiety > SATIETY_MAX || !Number.isInteger(s.satiety)) problems.push(`${s.name} has satiety ${s.satiety}`);
    // Between ticks every living survivor has health; at 0 they are removed within the tick.
    if (s.health <= 0 || s.health > HEALTH_MAX || !Number.isInteger(s.health)) problems.push(`${s.name} has health ${s.health}`);
    if (s.treatment) {
      const bandage = items.get(s.treatment.itemId);
      if (!bandage || (bandage.kind !== 'bandage' && bandage.kind !== 'medicine')) problems.push(`${s.name} is treating with missing bandage ${s.treatment.itemId}`);
      else if (bandage.reservedBy !== s.id) problems.push(`${s.name} uses ${bandage.id}, reserved by ${bandage.reservedBy}`);
      if (s.jobId) problems.push(`${s.name} is treating wounds but holds job ${s.jobId}`);
      if (s.sleep || s.meal) problems.push(`${s.name} is treating wounds while sleeping or eating`);
    }
    if (s.weapon) {
      const weapon = items.get(s.weapon);
      if (!weapon || (weapon.kind !== 'weapon' && weapon.kind !== 'gun')) problems.push(`${s.name} wields missing weapon ${s.weapon}`);
      else if (weapon.carriedBy !== s.id) problems.push(`${s.name} wields ${weapon.id}, carried by ${weapon.carriedBy}`);
    }
    if (s.arming) {
      const weapon = items.get(s.arming);
      if (!weapon || (weapon.kind !== 'weapon' && weapon.kind !== 'gun' && weapon.kind !== 'ammo')) problems.push(`${s.name} is fetching missing weapon ${s.arming}`);
      else if (weapon.reservedBy !== s.id || !weapon.pos) problems.push(`${s.name} is fetching ${weapon.id}, which isn't reserved on the ground`);
      if (s.jobId || s.sleep || s.meal || s.treatment) problems.push(`${s.name} is fetching a weapon while busy with something else`);
    }
    for (const slot of ['body', 'feet'] as const) {
      const id = s[slot];
      const worn = id ? items.get(id) : undefined;
      if (id && (!worn || worn.carriedBy !== s.id || !isGarment(worn.kind) || SLOT_OF[worn.kind] !== slot)) problems.push(`${s.name} wears ${id} on ${slot}, which doesn't fit`);
    }
    if (s.dressing) {
      const item = items.get(s.dressing);
      if (!item || item.reservedBy !== s.id || !item.pos) problems.push(`${s.name} is fetching ${s.dressing}, which isn't reserved on the ground`);
      if (s.jobId || s.sleep || s.meal || s.treatment || s.arming) problems.push(`${s.name} is fetching clothing while busy with something else`);
    }
    if (s.leisure) {
      if (s.jobId || s.sleep || s.meal || s.treatment || s.arming || s.breakdown) problems.push(`${s.name} is relaxing while busy with something else`);
      if (!isWalkable(grid, s.leisure.x, s.leisure.y)) problems.push(`${s.name} relaxes on unwalkable (${s.leisure.x}, ${s.leisure.y})`);
      if (state.survivors.some((o) => o !== s && o.leisure?.x === s.leisure!.x && o.leisure.y === s.leisure!.y)) problems.push(`${s.name} shares a rec room spot`);
    }
    if (s.infection !== null && (s.infection < 0 || !Number.isInteger(s.infection))) problems.push(`${s.name} has infection ${s.infection}`);
    for (const [id, v] of Object.entries(s.opinions)) {
      if (!survivors.has(id) || id === s.id || v < -100 || v > 100) problems.push(`${s.name} has an opinion ${v} of ${id}`);
    }
    if (s.breakdown && s.jobId) problems.push(`${s.name} holds job ${s.jobId} mid-breakdown`);
    if (s.meal) {
      const food = items.get(s.meal.itemId);
      if (!food || (food.kind !== 'food' && food.kind !== 'meal')) problems.push(`${s.name} is eating missing food ${s.meal.itemId}`);
      else if (food.reservedBy !== s.id) problems.push(`${s.name} eats ${food.id}, reserved by ${food.reservedBy}`);
      if (s.jobId) problems.push(`${s.name} is eating but holds job ${s.jobId}`);
      if (s.sleep) problems.push(`${s.name} is eating and sleeping at once`);
      const seat = s.meal.seat;
      if (seat && state.survivors.some((o) => o !== s && o.meal?.seat && o.meal.seat.x === seat.x && o.meal.seat.y === seat.y)) {
        problems.push(`${s.name} shares mess hall seat (${seat.x}, ${seat.y})`);
      }
    }
    if (!s.sleep) continue;
    if (s.jobId) problems.push(`${s.name} is sleeping but holds job ${s.jobId}`);
    if (s.carrying) problems.push(`${s.name} is sleeping while carrying ${s.carrying}`);
    if (s.sleep.bedId) {
      const bed = state.furniture.find((f) => f.id === s.sleep!.bedId);
      if (!bed) problems.push(`${s.name} sleeps in missing bed ${s.sleep.bedId}`);
      else if (bed.occupant !== s.id) problems.push(`${s.name} claims ${bed.id}, occupied by ${bed.occupant}`);
    }
  }
  const scavengeJobs = new Map<string, number>();
  for (const j of state.jobs) if (j.furnitureId) scavengeJobs.set(j.furnitureId, (scavengeJobs.get(j.furnitureId) ?? 0) + 1);
  for (const f of state.furniture) {
    if (f.loot < 0 || !Number.isInteger(f.loot)) problems.push(`${f.id} has loot ${f.loot}`);
    // Crates are unmarked when searched out; trees stay marked while bare (they grow back).
    if (f.scavenge && !(f.kind === 'tree' || (f.kind === 'crate' && f.loot > 0))) problems.push(`${f.id} marked for scavenging with nothing to find`);
    if ((scavengeJobs.get(f.id) ?? 0) > 1) problems.push(`${f.id} has ${scavengeJobs.get(f.id)} scavenge jobs`);
    if (f.occupant && f.kind !== 'bed') problems.push(`${f.kind} ${f.id} has an occupant`);
    if (f.owner && (f.kind !== 'bed' || !survivors.has(f.owner))) problems.push(`${f.kind} ${f.id} owned by ${f.owner}, who isn't here`);
    if (f.owner && f.occupant && f.occupant !== f.owner) problems.push(`${f.owner}'s bed ${f.id} is used by ${f.occupant}`);
    if (f.owner && state.furniture.some((g) => g !== f && g.owner === f.owner)) problems.push(`${f.owner} owns more than one bed`);
  }
  for (const f of state.furniture) {
    if (!f.occupant) continue;
    const s = survivors.get(f.occupant);
    if (s?.sleep?.bedId !== f.id) problems.push(`${f.id} occupied by ${f.occupant}, who isn't sleeping in it`);
  }

  const zombieIds = new Set<string>();
  for (const z of state.zombies) {
    if (zombieIds.has(z.id)) problems.push(`duplicate zombie id ${z.id}`);
    zombieIds.add(z.id);
    // Zombies stand on floor; raiders may also stand in an unlocked doorway.
    const standable = (x: number, y: number) => isPassable(grid, x, y, moverOf(z));
    if (!standable(z.pos.x, z.pos.y)) problems.push(`zombie ${z.id} on non-floor (${z.pos.x}, ${z.pos.y})`);
    if (z.move && !standable(z.move.to.x, z.move.to.y)) problems.push(`zombie ${z.id} stepping off floor`);
    if (z.health <= 0 || z.health > z.maxHealth) problems.push(`zombie ${z.id} has health ${z.health}`);
  }
  const guarded = new Map<string, number>();
  for (const j of state.jobs) {
    if (j.type !== 'guard') continue;
    if (!j.zombieId || !zombieIds.has(j.zombieId)) problems.push(`${j.id} hunts missing zombie ${j.zombieId}`);
    else guarded.set(j.zombieId, (guarded.get(j.zombieId) ?? 0) + 1);
  }
  for (const [id, n] of guarded) if (n > 1) problems.push(`zombie ${id} has ${n} guard jobs`);

  const plotTiles = new Set<string>();
  const tendedPlots = new Map<string, number>();
  for (const j of state.jobs) if (j.plotId) tendedPlots.set(j.plotId, (tendedPlots.get(j.plotId) ?? 0) + 1);
  for (const plot of state.farm) {
    const key = `${plot.x},${plot.y}`;
    if (plotTiles.has(key)) problems.push(`two farm plots on (${key})`);
    plotTiles.add(key);
    if (getTile(grid, plot.x, plot.y) !== 'floor') problems.push(`farm plot ${plot.id} on non-floor (${key})`);
    if (plot.growth < 0 || plot.growth > growTicks(plot) || (!plot.planted && plot.growth !== 0)) problems.push(`farm plot ${plot.id} has growth ${plot.growth}`);
    if ((tendedPlots.get(plot.id) ?? 0) > 1) problems.push(`farm plot ${plot.id} has ${tendedPlots.get(plot.id)} jobs`);
  }

  for (const w of state.wanderers) {
    if (!isWalkable(grid, w.x, w.y)) problems.push(`wanderer ${w.name} on unwalkable (${w.x}, ${w.y})`);
  }

  const listed = (arr: number[], list: number[], name: string) => {
    const want = arr.flatMap((v, i) => (v !== 0 ? [i] : []));
    const have = [...list].sort((a, b) => a - b);
    if (want.length !== have.length || want.some((v, k) => v !== have[k])) problems.push(`${name} list out of step with the map`);
  };
  listed(grid.dirt, grid.dirtList, 'dirt');
  listed(grid.damage, grid.damageList, 'damage');
  for (let i = 0; i < grid.damage.length; i++) {
    const d = grid.damage[i]!;
    if (d === 0) continue;
    const tile = grid.tiles[i]!;
    const at = `(${i % grid.width}, ${Math.floor(i / grid.width)})`;
    if (tile !== 'wall' && tile !== 'door' && tile !== 'barricade') problems.push(`damage ${d} on ${tile} ${at}`);
    else if (d < 0 || d >= STRUCTURE_HEALTH[tile] || !Number.isInteger(d)) problems.push(`${tile} at ${at} has damage ${d}`);
  }

  const fireTiles = new Set<string>();
  for (const f of state.fires) {
    const key = `${f.x},${f.y}`;
    if (fireTiles.has(key) || !inBounds(grid, f.x, f.y) || f.left <= 0) problems.push(`fire at (${key}) is doubled, off the map or out`);
    fireTiles.add(key);
  }
  for (const s of state.survivors) {
    if (s.hydration < 0 || s.hydration > HYDRATION_MAX || !Number.isInteger(s.hydration)) problems.push(`${s.name} has hydration ${s.hydration}`);
    if (s.drink && (s.jobId || s.sleep || s.meal || s.treatment || s.arming || s.dressing)) problems.push(`${s.name} is drinking while busy with something else`);
  }
  for (const s of state.survivors) {
    if (s.reading) {
      const book = items.get(s.reading.itemId);
      if (!book || book.kind !== 'book' || book.reservedBy !== s.id || !book.pos) problems.push(`${s.name} is reading ${s.reading.itemId}, which isn't a reserved book on the ground`);
      if (s.jobId || s.sleep || s.meal || s.treatment || s.arming || s.dressing || s.drink || s.leisure) problems.push(`${s.name} is reading while busy with something else`);
    }
    if (s.bitten !== null && s.infection === null) problems.push(`${s.name} has a bite with no infection`);
    if (s.childUntil !== null && s.jobId) problems.push(`${s.name} is a child with job ${s.jobId}`);
    if (s.cartLoad.length > 0 && (!s.jobId || !s.cart || !s.carrying)) problems.push(`${s.name} has a loaded cart with no haul on`);
    if (s.cart && (items.get(s.cart)?.kind !== 'cart' || items.get(s.cart)?.carriedBy !== s.id)) problems.push(`${s.name} pushes ${s.cart}, which isn't their cart`);
  }
  for (const a of state.animals) if (getTile(grid, a.pos.x, a.pos.y) !== 'floor') problems.push(`animal ${a.id} off the floor`);
  for (const p of state.prisoners) if (!isWalkable(grid, p.x, p.y)) problems.push(`prisoner ${p.id} in a wall`);
  for (const d of state.dogs) if (!isWalkable(grid, d.pos.x, d.pos.y) || d.health <= 0) problems.push(`dog ${d.id} is in a wall or dead`);
  for (const s of state.survivors) if (s.sick !== null && (s.sick <= 0 || !Number.isInteger(s.sick))) problems.push(`${s.name} has sickness ${s.sick}`);
  for (const i of grid.locked) if (grid.tiles[i] !== 'door') problems.push(`lock on a ${grid.tiles[i]} at ${i}`);
  for (const d of state.dormant) if (!inBounds(grid, d.x, d.y)) problems.push(`dormant zombie ${d.id} off the map`);
  for (const h of state.herds) if (!inBounds(grid, h.x, h.y) || !inBounds(grid, h.to.x, h.to.y) || h.size <= 0) problems.push(`herd ${h.id} is off the map or empty`);
  for (const a of state.exploreAreas) {
    if (a.x0 > a.x1 || a.y0 > a.y1 || a.x0 < 0 || a.y0 < 0 || a.x1 >= grid.width || a.y1 >= grid.height) problems.push(`explore area ${a.id} is not a box on the map`);
  }

  const roomsClaimed = new Set<number>();
  for (const d of state.roomDesignations) {
    if (getTile(grid, d.x, d.y) !== 'floor') problems.push(`room designation ${d.id} anchored on non-floor`);
    const label = labelAt(state, d.x, d.y);
    if (label >= 0 && roomsClaimed.has(label)) problems.push(`room ${label} has two designations`);
    roomsClaimed.add(label);
  }

  grid.dirt.forEach((d, i) => {
    if (d === 1 && getTile(grid, i % grid.width, Math.floor(i / grid.width)) !== 'floor') {
      problems.push(`dirt on non-floor tile ${i}`);
    }
  });

  return problems;
}
