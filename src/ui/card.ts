// The character card, in two parts (DOM; reads state only):
//   status strip    top right: an icon for each condition that matters right now (bleeding,
//                   panicking, soaked, blood up...), coloured by how bad it is, with the cause and
//                   the effect on hover. Nothing shows when nothing is wrong.
//   character panel bottom left: who you control (or have selected), what they're doing and
//                   want, and bars for the body's needs, with mood, social, comfort and panic under.
// conditionsOf and barsOf are plain functions of state, so tests can check them without a DOM.

import { COLD, FREEZING, PANICKING, SOAKED, WINDED } from '../sim/body';
import { CLUTCHED_MIN, CLUTCH_PCT, clutchedBy } from '../sim/control';
import { HUNGRY, SATIETY_MAX } from '../sim/food';
import { HEALTH_MAX, isCritical, isHurt } from '../sim/health';
import { getJob } from '../sim/jobs';
import { BOOSTS } from '../sim/moodlets';
import { THOUGHTS, isMiserable, mood, moodFactors } from '../sim/mood';
import { describeOrder } from '../sim/orders';
import { REST_MAX, TIRED } from '../sim/rest';
import { describeResolve, isBreaking, isSteeled } from '../sim/resolve';
import type { SimState } from '../sim/state';
import { STORY_JOBS } from '../sim/story';
import type { Survivor } from '../sim/survivors';
import { HYDRATION_MAX, THIRSTY } from '../sim/water';
import { LOW } from '../sim/wellbeing';
import { describeWish } from '../sim/wishes';
import type { ViewState } from './view';

export type Tone = 'good' | 'mild' | 'bad';

export interface Condition {
  id: string;
  label: string;
  /** A one-character glyph for the icon. */
  glyph: string;
  tone: Tone;
  /** Why, and what it does (the tooltip). */
  about: string;
}

/** Everything notable about a survivor right now, worst first. */
export function conditionsOf(state: SimState, s: Survivor): Condition[] {
  const out: Condition[] = [];
  const add = (id: string, label: string, glyph: string, tone: Tone, about: string) => out.push({ id, label, glyph, tone, about });
  if (s.bleeding) add('bleeding', 'Bleeding', '✚', 'bad', 'Losing blood until a bandage is on it.');
  if (s.bitten) add('bitten', 'Bitten', '☣', 'bad', `Bitten on the ${s.bitten}. Taking it off might still save them.`);
  else if (s.infection !== null) add('infected', 'Infected', '☣', 'bad', 'The fever is in them. It ends one way.');
  if (isCritical(s.health)) add('critical', 'Badly hurt', '♥', 'bad', 'Close to death: slow, and needs rest and treatment.');
  else if (isHurt(s.health)) add('hurt', 'Hurt', '♥', 'mild', 'Walks and works slower until healed.');
  if (s.wound) add('wound', `Hurt ${s.wound.limb}`, '✚', 'mild', `A wounded ${s.wound.limb}, healing.`);
  if (s.sick !== null) add('sick', 'Sick', '☁', 'mild', 'Something they ate or drank. It will pass.');
  const clutching = clutchedBy(state, s);
  if (clutching > 0) {
    const slowed = Math.min(100 - CLUTCHED_MIN, CLUTCH_PCT * clutching);
    add('grabbed', clutching > 1 ? `Grabbed (${clutching})` : 'Grabbed', '✋', clutching > 1 ? 'bad' : 'mild', `The dead within reach drag at you: ${slowed}% slower. Hit them to break free.`);
  }
  if (s.panic > PANICKING) add('panicking', 'Panicking', '!', 'bad', 'Some swings miss, and it weighs on mood. Ebbs when the dead are gone.');
  if (s.satiety === 0) add('starving', 'Starving', '◒', 'bad', 'Losing health for want of food.');
  else if (s.satiety < HUNGRY) add('hungry', 'Hungry', '◒', 'mild', 'Needs to eat soon.');
  if (state.harsh && s.hydration === 0) add('dehydrated', 'Dehydrated', '◊', 'bad', 'Losing health for want of water.');
  else if (state.harsh && s.hydration < THIRSTY) add('thirsty', 'Thirsty', '◊', 'mild', 'Needs a drink, and it weighs on mood.');
  if (s.bodyHeat < FREEZING) add('freezing', 'Freezing', '❄', 'bad', 'Losing health to the cold. Get indoors, by a fire, into a coat.');
  else if (s.bodyHeat < COLD) add('cold', 'Cold', '❄', 'mild', 'Walks slower, and it weighs on mood.');
  if (s.wetness > SOAKED) add('soaked', 'Soaked', '≈', 'mild', 'Wet through: cold comes quicker at night. Dries indoors or by warmth.');
  if (s.rest < TIRED) add('tired', s.rest === 0 ? 'Exhausted' : 'Tired', 'z', s.rest === 0 ? 'bad' : 'mild', 'Needs sleep.');
  if (s.endurance < WINDED) add('winded', 'Winded', '~', 'mild', 'No running; swings come later and land softer. Comes back at rest.');
  if (isBreaking(s)) add('breaking', 'Breaking', '▼', 'bad', 'Close to giving up: they may walk away at dawn.');
  else if (isSteeled(s)) add('steeled', 'Steeled', '▲', 'good', 'Hard to shake: panic comes slower.');
  if (s.thoughts.some((t) => t.kind === 'irradiated')) add('irradiated', 'Irradiated', '☢', 'bad', THOUGHTS.irradiated.label);
  if (isMiserable(state, s)) add('miserable', 'Miserable', '▽', 'mild', 'Works slower until their mood lifts.');
  if (s.social < LOW) add('lonely', 'Lonely', '…', 'mild', 'Needs company and someone to talk to.');
  if (s.comfort < LOW) add('uncomfortable', 'Uncomfortable', '□', 'mild', 'Needs somewhere decent to sleep and sit.');
  for (const t of s.thoughts) {
    if (!BOOSTS[t.kind] || out.some((c) => c.id === t.kind)) continue;
    const label = THOUGHTS[t.kind].label;
    const name = label.replace(/ \(.*\)$/, '');
    add(t.kind, name.charAt(0).toUpperCase() + name.slice(1), THOUGHTS[t.kind].effect > 0 ? '▲' : '▽', THOUGHTS[t.kind].effect > 0 ? 'good' : 'mild', label);
  }
  const rank: Record<Tone, number> = { bad: 0, mild: 1, good: 2 };
  return out.sort((a, b) => rank[a.tone] - rank[b.tone]);
}

export interface Bar {
  label: string;
  /** 0..100. */
  pct: number;
  /** The bar's own colour while it's fine. */
  kind: 'health' | 'food' | 'water' | 'rest' | 'endurance' | 'warmth';
  /** Under this it shows amber; under `crit`, red. */
  low: number;
  crit: number;
}

const pct = (v: number, max: number) => Math.max(0, Math.min(100, Math.round((100 * v) / max)));

/** The body's needs as bars (water only in a harsh game, where thirst counts). */
export function barsOf(state: SimState, s: Survivor): Bar[] {
  return [
    { label: 'Health', pct: pct(s.health, HEALTH_MAX), kind: 'health', low: 60, crit: 30 },
    { label: 'Food', pct: pct(s.satiety, SATIETY_MAX), kind: 'food', low: pct(HUNGRY, SATIETY_MAX), crit: 5 },
    ...(state.harsh ? [{ label: 'Water', pct: pct(s.hydration, HYDRATION_MAX), kind: 'water', low: pct(THIRSTY, HYDRATION_MAX), crit: 5 } as Bar] : []),
    { label: 'Rest', pct: pct(s.rest, REST_MAX), kind: 'rest', low: pct(TIRED, REST_MAX), crit: 5 },
    { label: 'Endurance', pct: s.endurance, kind: 'endurance', low: WINDED, crit: 10 },
    { label: 'Warmth', pct: s.bodyHeat, kind: 'warmth', low: COLD, crit: FREEZING },
  ];
}

/** "Mood 55: ate at a table +10, hurt -10" (a tooltip, and what the debug panel shows). */
export function moodTitle(state: SimState, s: Survivor): string {
  const factors = moodFactors(state, s).map(([label, effect]) => `${label} ${effect > 0 ? '+' : ''}${effect}`);
  return `Mood ${mood(state, s)}${isMiserable(state, s) ? ' (miserable: works slower)' : ''}${factors.length ? `: ${factors.join(', ')}` : ''}`;
}

const BUSY = { eat: 'eating', search: 'searching', drink: 'drinking', sleep: 'asleep', treat: 'treating', build: 'building', repair: 'repairing', forage: 'foraging', cook: 'cooking' } as const;

/** What they're doing, in a few words. */
export function activity(state: SimState, s: Survivor): string {
  if (state.control?.survivorId === s.id) {
    const busy = state.control.busy;
    if (busy) return `${BUSY[busy.what]}…`;
    if (state.control.running && (state.control.dx || state.control.dy)) return s.endurance < WINDED ? 'too winded to run' : 'running';
    return s.note ?? '';
  }
  if (s.meal) return s.meal.progress > 0 ? (s.meal.seat ? 'eating at a table' : 'eating') : s.carrying ? 'taking food to a table' : 'going to eat';
  if (s.treatment) return s.treatment.progress > 0 ? 'treating themselves' : 'going for a remedy';
  if (s.arming) return 'going for a weapon';
  if (s.dressing) return 'going for clothing';
  if (s.drink) return s.drink.progress > 0 ? 'drinking' : 'going for a drink';
  if (s.reading) return s.reading.progress > 0 ? 'reading' : 'going to read';
  if (s.childUntil !== null && !s.sleep && !s.meal) return 'playing (a child)';
  if (s.breakdown) return 'having a breakdown';
  if (s.leisure) return s.leisure.progress > 0 ? 'relaxing' : 'going to relax';
  if (s.sleep && isHurt(s.health)) return s.sleep.asleep ? 'resting (hurt)' : 'going to rest (hurt)';
  if (s.sleep) return s.sleep.asleep ? `asleep (${s.sleep.bedId ? 'bed' : 'floor'})` : 'going to bed';
  const job = s.jobId ? getJob(state, s.jobId) : undefined;
  if (job) return `${job.type} (${job.x}, ${job.y})`;
  if (s.goal) return s.order?.kind === 'follow' ? 'following' : s.order?.kind === 'loot' ? 'going to loot' : s.order ? 'going back to their post' : 'walking (ordered)';
  return s.order ? describeOrder(s.order) : 'idle';
}

/** Who the card is about: the one you control, else the one selected. */
function subject(state: SimState, view: ViewState): Survivor | undefined {
  const id = state.control?.survivorId ?? view.selectedId;
  return id ? state.survivors.find((s) => s.id === id) : undefined;
}

export function createStatusStrip(view: ViewState) {
  const root = el('div', 'hud-conditions');
  let shown = '';
  return {
    root,
    update(state: SimState) {
      const s = subject(state, view);
      const list = s ? conditionsOf(state, s) : [];
      const key = list.map((c) => c.id + c.label).join('|');
      root.hidden = list.length === 0;
      if (key === shown) return;
      shown = key;
      root.replaceChildren(
        ...list.map((c) => {
          const row = el('div', `hud-condition ${c.tone}`);
          row.title = c.about;
          row.append(el('span', 'hud-condition-glyph', c.glyph), el('span', '', c.label));
          return row;
        }),
      );
    },
  };
}

export function createCharacterPanel(view: ViewState) {
  const root = el('div', 'hud-card');
  const head = el('div', 'hud-card-head');
  const name = el('span', 'hud-card-name');
  const about = el('span', 'hud-card-about');
  head.append(name, about);
  const doing = el('div', 'hud-card-doing');
  const bars = el('div', 'hud-card-bars');
  const minds = el('div', 'hud-card-minds');
  root.append(head, doing, bars, minds);
  const barEls = new Map<string, { root: HTMLElement; value: HTMLElement; fill: HTMLElement }>();
  let barKey = '';
  let textKey = '';

  return {
    root,
    update(state: SimState) {
      const s = subject(state, view);
      root.hidden = !s;
      if (!s) return;
      const list = barsOf(state, s);
      const labels = list.map((b) => b.label).join('|');
      if (labels !== barKey) {
        barKey = labels;
        barEls.clear();
        bars.replaceChildren(
          ...list.map((b) => {
            const row = el('div', 'hud-card-bar');
            const top = el('div', 'hud-card-bar-top');
            const value = el('span', '');
            top.append(el('span', '', b.label), value);
            const track = el('div', 'hud-card-track');
            const fill = el('div', `hud-card-fill ${b.kind}`);
            track.append(fill);
            row.append(top, track);
            barEls.set(b.label, { root: row, value, fill });
            return row;
          }),
        );
      }
      for (const b of list) {
        const e = barEls.get(b.label)!;
        const text = String(b.pct);
        if (e.value.textContent !== text) {
          e.value.textContent = text;
          e.fill.style.width = `${b.pct}%`;
          e.fill.classList.toggle('low', b.pct < b.low && b.pct >= b.crit);
          e.fill.classList.toggle('crit', b.pct < b.crit);
        }
      }

      const you = state.control?.survivorId === s.id;
      const job = STORY_JOBS[s.story.job] ?? '';
      const now = activity(state, s);
      const wish = s.wish ? `wants ${describeWish(state, s.wish)}${s.wish.promised ? ' (promised)' : ''}` : '';
      const m = mood(state, s);
      const key = [s.name, you, job, s.age, s.resolve, now, wish, m, s.social, s.comfort, s.panic].join('|');
      if (key === textKey) return;
      textKey = key;
      name.textContent = s.name + (you ? ' (you)' : '');
      about.textContent = `${job.replace(/^an? /, '')}, ${s.age}`;
      doing.textContent = [now, wish].filter((x) => x !== '').join(' · ');
      doing.hidden = doing.textContent === '';
      const stat = (label: string, value: number, bad: boolean, title?: string) => {
        const span = el('span', `hud-card-mind${bad ? ' bad' : ''}`, `${label} ${value}`);
        if (title) span.title = title;
        return span;
      };
      minds.replaceChildren(
        stat('Resolve', s.resolve, isBreaking(s), `${describeResolve(s)}: the will to keep going. Drifts with mood each dawn.`),
        stat('Mood', m, isMiserable(state, s), moodTitle(state, s)),
        stat('Social', s.social, s.social < LOW),
        stat('Comfort', s.comfort, s.comfort < LOW),
        stat('Panic', s.panic, s.panic > PANICKING),
      );
    },
  };
}

function el<K extends keyof HTMLElementTagNameMap>(tag: K, className: string, text?: string): HTMLElementTagNameMap[K] {
  const node = document.createElement(tag);
  node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
}
