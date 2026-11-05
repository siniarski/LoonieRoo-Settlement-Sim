// The bag and the loot list (inventory.ts), for the survivor you control (DOM; reads state only).
//   bag    above the minimap: weight against what it holds, and each item with its quick slot
//          (keys 1 to 9 use the first nine: input.ts), use and drop
//   loot   a searched container beside you with things left in it: take one, or take all

import type { Command } from '../sim/commands';
import { controlled } from '../sim/control';
import { ITEM_WEIGHT, PACK_MAX, contents, packWeight } from '../sim/inventory';
import { getItem } from '../sim/items';
import { furnitureAt, type Furniture } from '../sim/furniture';
import type { SimState } from '../sim/state';

const kg = (tenths: number) => (tenths / 10).toFixed(1);

/** What a container looks like, for the loot list's title. */
const NAMES: Record<string, string> = {
  counter: 'Counter', icebox: 'Fridge', shelf: 'Shelf', wardrobe: 'Wardrobe', dresser: 'Dresser', cabinet: 'Cabinet', toolbox: 'Toolbox',
  locker: 'Locker', desk: 'Desk', pallet: 'Pallet', sack: 'Sacks', crate: 'Crate',
};

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
      // The bag.
      const weight = packWeight(state, s);
      const items = s.pack.map((id) => getItem(state, id)).filter((i) => i !== undefined);
      const bagKey = `${weight}|${items.map((i) => i.id).join(',')}`;
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
          return row;
        });
        root.replaceChildren(head, bar, ...(rows.length ? rows : [el('div', 'hud-bag-empty', 'empty: pick things up, or search a container')]));
      }
      // A searched container beside you with things left in it.
      // (The nine tiles round you by index, not a scan of every piece of furniture in the world, every frame.)
      let box: Furniture | undefined;
      for (let dy = -1; dy <= 1 && !box; dy++) {
        for (let dx = -1; dx <= 1 && !box; dx++) {
          const f = furnitureAt(state, s.pos.x + dx, s.pos.y + dy);
          if (f?.kind === 'crate' && contents(state, f).length > 0) box = f;
        }
      }
      if (!box) return;
      loot.hidden = false;
      const inside = contents(state, box);
      const lootKey = `${box.id}|${inside.map((i) => i.id).join(',')}`;
      if (lootKey === shownLoot) return;
      shownLoot = lootKey;
      const head = el('div', 'hud-bag-head', NAMES[box.look ?? 'crate'] ?? 'Container');
      const rows = inside.map((item) => {
        const row = el('div', 'hud-bag-row');
        row.append(el('span', 'hud-bag-name', item.kind), el('span', 'hud-bag-kg', kg(ITEM_WEIGHT[item.kind])), button('take', () => command({ type: 'take', itemId: item.id })));
        return row;
      });
      loot.replaceChildren(head, ...rows, button('take all', () => command({ type: 'takeAll', containerId: box.id })));
    },
  };
}

function el<K extends keyof HTMLElementTagNameMap>(tag: K, className: string, text?: string): HTMLElementTagNameMap[K] {
  const node = document.createElement(tag);
  node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
}
