// Job types and priorities. Kept separate from jobs.ts so survivors.ts can
// use them without an import cycle.

export type JobType = 'guard' | 'build' | 'farm' | 'haul' | 'scavenge' | 'clean';

/** Also the tie-break order when two job types share a priority: threats first. */
export const JOB_TYPES: readonly JobType[] = ['guard', 'build', 'farm', 'haul', 'scavenge', 'clean'];

/** 1 is highest, 4 lowest, 0 means the survivor won't do this job type. */
export type Priority = 0 | 1 | 2 | 3 | 4;

export type Priorities = Record<JobType, Priority>;

export const DEFAULT_PRIORITIES: Priorities = { guard: 3, build: 3, farm: 3, haul: 3, scavenge: 3, clean: 3 };

/** All job types off: handy for tests that want a survivor to do nothing but move. */
export const NO_JOBS: Priorities = { guard: 0, build: 0, farm: 0, haul: 0, scavenge: 0, clean: 0 };

export function isPriority(n: unknown): n is Priority {
  return n === 0 || n === 1 || n === 2 || n === 3 || n === 4;
}

export function isJobType(t: unknown): t is JobType {
  return typeof t === 'string' && (JOB_TYPES as readonly string[]).includes(t);
}
