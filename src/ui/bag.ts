// The bag and the loot list (inventory.ts), for the survivor you control (DOM; reads state only).
//   bag    above the minimap: weight against what it holds, and each item with its quick slot
//          (keys 1 to 9 use the first nine: input.ts), use and drop; with a container beside you,
//          stash (into it)
//   loot   a container beside you: a searched one with things left in it, or a car's boot (shown
//          even when empty, so there's somewhere to stash things): take one, or take all

import type { Command } from '../sim/commands';
import { controlled } from '../sim/control';
import { ITEM_WEIGHT, PACK_MAX, containerOf, contents, packWeight } from '../sim/inventory';
import { getItem } from '../sim/items';
import { containerName, furnitureAt } from '../sim/furniture';
import { SUB } from '../sim/motion';
import type { SimState } from '../sim/state';
import { bootMax, vehicleNear, vehicleOf } from '../sim/vehicles';

const kg = (tenths: number) => (tenths / 10).toFixed(1);
const capitalise = (s: string) => s[0]!.toUpperCase() + s.slice(1);


/** The container beside you, if any: a car's boot, or a crate with things in it. */
function beside(state: SimState, s: NonNullable<ReturnType<typeof controlled>>): { id: string; title: string } | null {
  if (vehicleOf(state, s)) return null;
  const car = vehicleNear(state, s.at, SUB * 1.5);
  if (car) {
    const load = contents(state, car).reduce((sum, i) => sum + ITEM_WEIGHT[i.kind], 0);
    return { id: car.id, title: `${car.kind === 'truck' ? "Truck's" : "Car's"} boot  ${kg(load)} / ${kg(bootMax(car))} kg` };
  }
  // (The nine tiles round you by index, not a scan of every piece of furniture in the world, every frame.)
  for (let dy = -1; dy <= 1; dy++) {
    for (let dx = -1; dx <= 1; dx++) {
      const f = furnitureAt(state, s.pos.x + dx, s.pos.y + dy);
      if (f?.kind === 'crate' && contents(state, f).length > 0) return { id: f.id, title: capitalise(containerName(f)) };
    }
  }
  return null;
}

export function createBag(command: (cmd: Command) => void) {
  const root = el('div', 'hud-bag');
  const loot = el('div', 'hud-loot');
  let shownBag = '';
  let shownLoot = '';
  const button = (label: string, onClick: () => void) => {
    const b = el('button', 'hud-button hud-small', label);
    b.addEventListener('click', onClick);
    return b;
  };
  return {
    roots: [root, loot],
    update(state: SimState) {
      const s = controlled(state);
      root.hidden = !s;
      loot.hidden = true;
      if (!s) return;
      const box = beside(state, s);
      // The bag.
      const weight = packWeight(state, s);
      const items = s.pack.map((id) => getItem(state, id)).filter((i) => i !== undefined);
      const bagKey = `${weight}|${items.map((i) => i.id).join(',')}|${box?.id ?? ''}`;
      if (bagKey !== shownBag) {
        shownBag = bagKey;
        const head = el('div', 'hud-bag-head', `Bag  ${kg(weight)} / ${kg(PACK_MAX)} kg`);
        const bar = el('div', 'hud-card-track');
        const fill = el('div', `hud-card-fill ${weight > PACK_MAX * 0.85 ? 'low' : 'endurance'}`);
        fill.style.width = `${Math.min(100, (100 * weight) / PACK_MAX)}%`;
        bar.append(fill);
        const rows = items.map((item, i) => {
          const row = el('div', 'hud-bag-row');
          row.append(el('span', 'hud-bag-slot', i < 9 ? String(i + 1) : ''), el('span', 'hud-bag-name', item.kind), el('span', 'hud-bag-kg', kg(ITEM_WEIGHT[item.kind])));
          row.append(button('use', () => command({ type: 'useItem', itemId: item.id })), button('drop', () => command({ type: 'drop', itemId: item.id })));
          if (box) row.append(button('stash', () => command({ type: 'stash', itemId: item.id, containerId: box.id })));
          return row;
        });
        root.replaceChildren(head, bar, ...(rows.length ? rows : [el('div', 'hud-bag-empty', 'empty: pick things up, or search a container')]));
      }
      if (!box || !containerOf(state, box.id)) return;
      loot.hidden = false;
      const inside = contents(state, { id: box.id });
      const lootKey = `${box.id}|${box.title}|${inside.map((i) => i.id).join(',')}`;
      if (lootKey === shownLoot) return;
      shownLoot = lootKey;
      const head = el('div', 'hud-bag-head', box.title);
      const rows = inside.map((item) => {
        const row = el('div', 'hud-bag-row');
        row.append(el('span', 'hud-bag-name', item.kind), el('span', 'hud-bag-kg', kg(ITEM_WEIGHT[item.kind])), button('take', () => command({ type: 'take', itemId: item.id })));
        return row;
      });
      if (inside.length === 0) rows.push(el('div', 'hud-bag-empty', 'empty: stash things from your bag'));
      loot.replaceChildren(head, ...rows, ...(inside.length ? [button('take all', () => command({ type: 'takeAll', containerId: box.id }))] : []));
    },
  };
}

function el<K extends keyof HTMLElementTagNameMap>(tag: K, className: string, text?: string): HTMLElementTagNameMap[K] {
  const node = document.createElement(tag);
  node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
}
