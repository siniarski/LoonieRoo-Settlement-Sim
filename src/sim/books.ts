// Books, found in ruins. Each has a subject (item.tag, from a hash of its id):
// most teach a skill (a level's worth of practice to whoever reads it), and
// some teach the colony something new (state.knowledge), which some recipes
// need (crafting.ts). A survivor with nothing to do reads the nearest free
// book for READ_TICKS; a job turning up interrupts them (they come back to it).
// A book is used up once read.

import { logEvent } from './events';
import { hashRoll, idNumber } from './hash';
import { getItem, removeItem, type Item } from './items';
import { IDLE_RETRY_TICKS, seekJob } from './jobs';
import type { JobType } from './jobTypes';
import { nearestFreeItem } from './needs';
import { findPathToAny } from './pathfinding';
import { XP_PER_LEVEL } from './skills';
import { samePoint } from './space';
import type { SimState } from './state';
import { walk, type Survivor } from './survivors';

export type Knowledge = 'gunsmithing' | 'tailoring';
export const KNOWLEDGE: Record<Knowledge, string> = {
  gunsmithing: 'hand-loading ammo and making guns',
  tailoring: 'sewing padded armour',
};
const SKILL_BOOKS: readonly JobType[] = ['build', 'farm', 'craft', 'cook', 'doctor', 'guard'];
/** One book in this many is about something new rather than a skill. */
export const KNOWLEDGE_BOOK_ONE_IN = 4;
export const READ_TICKS = 300;

/** A book's subject, from its id. */
export function bookSubject(id: string): string {
  const roll = hashRoll(idNumber(id), 0xb00c);
  if (roll < 1 / KNOWLEDGE_BOOK_ONE_IN) return (Object.keys(KNOWLEDGE) as Knowledge[])[Math.floor(roll * KNOWLEDGE_BOOK_ONE_IN * 2) % 2]!;
  return SKILL_BOOKS[Math.floor(roll * 1000) % SKILL_BOOKS.length]!;
}

export function knows(state: SimState, k: Knowledge): boolean {
  return state.knowledge.includes(k);
}

/** "a book on farming", "a book on gunsmithing". */
export function describeBook(item: Item): string {
  return `a book on ${item.tag ?? 'nothing much'}`;
}

/** Idle with nothing to do: go and read a free book. Returns true if they set off. */
export function startReading(state: SimState, s: Survivor): boolean {
  if (s.reading || s.breakdown) return false;
  const book = nearestFreeItem(state, s, 'book');
  if (!book?.pos) return false;
  book.reservedBy = s.id;
  s.reading = { itemId: book.id, progress: 0 };
  s.note = `going to read ${describeBook(book)}`;
  return true;
}

/** Runs when standing still, with leisure. Returns true if reading handled this tick. */
export function updateReading(state: SimState, s: Survivor): boolean {
  if (!s.reading) return false;
  // Work comes first.
  if (state.tick >= s.nextSeekTick) {
    s.nextSeekTick = state.tick + IDLE_RETRY_TICKS;
    const was = s.reading;
    stopReading(state, s, 'put the book down for work');
    if (seekJob(state, s)) return true;
    const book = getItem(state, was.itemId);
    if (book?.pos && book.reservedBy === null) {
      book.reservedBy = s.id;
      s.reading = was;
    } else return false;
  }
  const book = getItem(state, s.reading.itemId);
  if (!book?.pos || book.reservedBy !== s.id) {
    stopReading(state, s, 'book gone');
    return false;
  }
  if (!samePoint(s.pos, book.pos)) {
    const path = findPathToAny(state.grid, s.pos, [book.pos]);
    if (!path || path.length === 0) {
      stopReading(state, s, "can't reach the book");
      return false;
    }
    walk(s, path, state.walkabilityVersion);
    return true;
  }
  s.reading.progress += 1;
  if (s.reading.progress === 1) s.note = `reading ${describeBook(book)}`;
  if (s.reading.progress < READ_TICKS) return true;
  s.reading = null;
  removeItem(state, book);
  learn(state, s, book.tag ?? '');
  return true;
}

function learn(state: SimState, s: Survivor, subject: string): void {
  if (subject in KNOWLEDGE) {
    const k = subject as Knowledge;
    if (!knows(state, k)) state.knowledge.push(k);
    s.note = `learned ${k}`;
    logEvent(state, `${s.name} read up on ${k}: the colony can now get on with ${KNOWLEDGE[k]}`);
    return;
  }
  const type = subject as JobType;
  s.xp[type] = (s.xp[type] ?? 0) + XP_PER_LEVEL;
  s.note = `read a book on ${type}`;
}

/** Puts the book back (unreserved). Safe to call when not reading. */
export function stopReading(state: SimState, s: Survivor, note: string): void {
  if (!s.reading) return;
  const book = getItem(state, s.reading.itemId);
  if (book && book.reservedBy === s.id) book.reservedBy = null;
  s.reading = null;
  s.goal = null;
  s.path = [];
  s.pathVersion = -1;
  s.note = note;
}
