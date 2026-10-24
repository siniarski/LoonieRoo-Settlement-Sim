// Consistency checks across survivors, jobs, items, and blueprints.
// Returns human-readable problems; empty means healthy. Used by tests after
// every tick and available to the debug panel. Read-only.

import { getTile, isWalkable } from './grid';
import type { SimState } from './state';

export function checkInvariants(state: SimState): string[] {
  const problems: string[] = [];
  const { grid } = state;
  const survivors = new Map(state.survivors.map((s) => [s.id, s]));
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
      if (!s.jobId) problems.push(`${s.name} carries ${s.carrying} with no job`);
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
    if (item.carriedBy && survivors.get(item.carriedBy)?.carrying !== item.id) {
      problems.push(`${item.id} says carriedBy ${item.carriedBy}, who isn't carrying it`);
    }
    if (item.reservedBy) {
      const holder = survivors.get(item.reservedBy);
      const job = holder?.jobId ? jobs.get(holder.jobId) : undefined;
      if (job?.itemId !== item.id) problems.push(`${item.id} reserved by ${item.reservedBy} without a matching haul job`);
    }
  }

  grid.dirt.forEach((d, i) => {
    if (d === 1 && getTile(grid, i % grid.width, Math.floor(i / grid.width)) !== 'floor') {
      problems.push(`dirt on non-floor tile ${i}`);
    }
  });

  return problems;
}
