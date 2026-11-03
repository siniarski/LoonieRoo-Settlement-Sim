// The alerts list (always on, top left under the status) and the colony
// overview (O): people, stock, storage filters, what's known, neighbours.
// Both only read state; buttons send commands.

import { alerts, foodDays, stockCounts, type Alert } from '../sim/alerts';
import { KNOWLEDGE } from '../sim/books';
import { describeClothing } from '../sim/clothing';
import type { Command } from '../sim/commands';
import { HEALTH_MAX } from '../sim/health';
import { getJob } from '../sim/jobs';
import { mood } from '../sim/mood';
import { STORE_CATEGORIES, designationStatus } from '../sim/rooms';
import { SATIETY_MAX } from '../sim/food';
import type { SimState } from '../sim/state';
import { HYDRATION_MAX } from '../sim/water';
import { threatScale } from '../sim/director';
import { CURE_SAMPLES } from '../sim/endgame';
import { dayOf } from '../sim/time';
import type { ViewState } from './view';

const MAX_ALERTS = 6;
const LEVEL_COLOUR: Record<Alert['level'], string> = { danger: '#ff7a6a', warn: '#ffd166', info: '#9fc3ff' };

function el<K extends keyof HTMLElementTagNameMap>(tag: K, className: string, text?: string): HTMLElementTagNameMap[K] {
  const node = document.createElement(tag);
  node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
}

/** Click an alert to look at where it is. */
export function createAlertList(view: ViewState) {
  const root = el('div', 'hud-alerts');
  let last = '';
  return {
    root,
    update(state: SimState) {
      const list = alerts(state);
      const key = JSON.stringify(list.slice(0, MAX_ALERTS));
      if (key === last) return;
      last = key;
      root.hidden = list.length === 0;
      root.replaceChildren();
      for (const a of list.slice(0, MAX_ALERTS)) {
        const row = el('div', 'hud-alert', `${a.level === 'danger' ? '▲' : a.level === 'warn' ? '●' : '·'} ${a.text}`);
        row.style.color = LEVEL_COLOUR[a.level];
        if (a.at) {
          const at = a.at;
          row.classList.add('clickable');
          row.title = `Look at (${at.x}, ${at.y})`;
          row.addEventListener('click', () => (view.camera = { ...view.camera, x: at.x + 0.5, y: at.y + 0.5 }));
        }
        root.append(row);
      }
      if (list.length > MAX_ALERTS) root.append(el('div', 'hud-alert', `… ${list.length - MAX_ALERTS} more (O: overview)`));
    },
  };
}

const pct = (v: number, max: number) => `${Math.round((100 * v) / max)}%`;

/** O: the whole colony on one panel. Rebuilt a few times a second at most. */
export function createOverview(view: ViewState, command: (cmd: Command) => void) {
  const root = el('div', 'hud-overview');
  let frame = 0;
  return {
    root,
    update(state: SimState) {
      root.hidden = !view.showOverview;
      if (!view.showOverview || frame++ % 15 !== 0) return;
      root.replaceChildren();
      root.append(el('div', 'hud-game-title', `Colony overview (O to close) · threat x${threatScale(state).toFixed(1)}`));

      // People.
      const table = el('table', 'hud-overview-table');
      const head = el('tr', '');
      for (const h of ['Name', 'Age', 'Health', 'Mood', 'Food', 'Water', 'Wearing', 'Doing']) head.append(el('th', '', h));
      table.append(head);
      for (const s of state.survivors) {
        const row = el('tr', '');
        const job = s.jobId ? getJob(state, s.jobId) : undefined;
        const doing = s.sleep ? 'asleep' : s.meal ? 'eating' : s.drink ? 'drinking' : s.reading ? 'reading' : job ? job.type : s.childUntil !== null ? 'playing' : 'idle';
        const cells = [
          s.name + (s.infection !== null ? ' (infected)' : s.sick !== null ? ' (sick)' : ''),
          s.childUntil !== null ? `${s.age} (child)` : String(s.age),
          pct(s.health, HEALTH_MAX),
          String(mood(state, s)),
          pct(s.satiety, SATIETY_MAX),
          state.harsh ? pct(s.hydration, HYDRATION_MAX) : '—',
          [describeClothing(state, s), s.weapon ? state.items.find((i) => i.id === s.weapon)?.kind : '', s.cart ? 'cart' : ''].filter(Boolean).join(', ') || '—',
          doing,
        ];
        for (const c of cells) row.append(el('td', '', c));
        table.append(row);
      }
      const away = state.expeditions.flatMap((x) => x.members.map((m) => m.name));
      root.append(el('div', 'hud-overview-head', `People: ${state.survivors.length}${away.length ? `, away: ${away.join(', ')}` : ''}${state.dogs.length ? `, dogs: ${state.dogs.map((d) => d.name).join(', ')}` : ''}`), table);

      // Stock.
      const days = foodDays(state);
      root.append(el('div', 'hud-overview-head', `Stock (food for ${Number.isFinite(days) ? days.toFixed(1) : '—'} days)`));
      root.append(el('div', 'hud-overview-text', stockCounts(state).map(([k, n]) => `${k} ${n}`).join(' · ') || 'nothing'));

      // Storage filters.
      const stores = state.roomDesignations.filter((d) => d.purpose === 'storage');
      if (stores.length > 0) {
        root.append(el('div', 'hud-overview-head', 'Storage rooms (click to take or refuse)'));
        for (const d of stores) {
          const row = el('div', 'hud-wanderer');
          row.append(el('span', 'hud-wanderer-label', `at (${d.x}, ${d.y})${designationStatus(state, d).complete ? '' : ' (incomplete)'}`));
          for (const c of STORE_CATEGORIES) {
            const takes = !d.refuses?.includes(c);
            const b = el('button', `hud-button${takes ? ' active' : ''}`, `${takes ? '✓' : '✗'} ${c}`);
            b.addEventListener('click', () => command({ type: 'setStorageFilter', x: d.x, y: d.y, category: c, accept: !takes }));
            row.append(b);
          }
          root.append(row);
        }
      }

      // Everything else.
      const eg = state.endgame;
      root.append(
        el('div', 'hud-overview-head', 'Endgame'),
        el('div', 'hud-overview-text', eg.won ? `Won: ${eg.won === 'cure' ? 'the cure' : 'evacuated'}` : `Cure: ${eg.cure}/${CURE_SAMPLES} samples studied (lab bench, powered) · Evacuation: ${eg.evacuation === null ? 'build a beacon and a radio, both powered' : `helicopter at dawn on day ${dayOf(eg.evacuation)}`}`),
      );
      const known = state.knowledge.map((k) => `${k} (${KNOWLEDGE[k]})`);
      root.append(el('div', 'hud-overview-head', 'Known'), el('div', 'hud-overview-text', known.join('; ') || 'nothing from books yet'));
      if (state.settlements.length > 0) {
        root.append(el('div', 'hud-overview-head', 'Neighbours'), el('div', 'hud-overview-text', state.settlements.map((n) => `${n.name} ${n.reputation}`).join(' · ')));
      }
      const all = alerts(state);
      root.append(el('div', 'hud-overview-head', `Alerts (${all.length})`));
      for (const a of all) {
        const row = el('div', 'hud-alert', a.text);
        row.style.color = LEVEL_COLOUR[a.level];
        root.append(row);
      }
    },
  };
}
