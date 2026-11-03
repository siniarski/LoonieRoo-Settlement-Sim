// Job types and priorities. Kept separate from jobs.ts so survivors.ts can
// use them without an import cycle.

export type JobType = 'guard' | 'firefight' | 'doctor' | 'build' | 'farm' | 'haul' | 'craft' | 'research' | 'cook' | 'scout' | 'scavenge' | 'hunt' | 'clean';

/** Also the tie-break order when two job types share a priority: threats first. */
export const JOB_TYPES: readonly JobType[] = ['guard', 'firefight', 'doctor', 'build', 'farm', 'haul', 'craft', 'research', 'cook', 'scout', 'scavenge', 'hunt', 'clean'];

/** 1 is highest, 4 lowest, 0 means the survivor won't do this job type. */
export type Priority = 0 | 1 | 2 | 3 | 4;

export type Priorities = Record<JobType, Priority>;

export const DEFAULT_PRIORITIES: Priorities = { guard: 3, firefight: 3, doctor: 3, build: 3, farm: 3, haul: 3, craft: 3, research: 3, cook: 3, scout: 3, scavenge: 3, hunt: 3, clean: 3 };

/** All job types off: handy for tests that want a survivor to do nothing but move. */
export const NO_JOBS: Priorities = { guard: 0, firefight: 0, doctor: 0, build: 0, farm: 0, haul: 0, craft: 0, research: 0, cook: 0, scout: 0, scavenge: 0, hunt: 0, clean: 0 };

export function isPriority(n: unknown): n is Priority {
  return n === 0 || n === 1 || n === 2 || n === 3 || n === 4;
}

export function isJobType(t: unknown): t is JobType {
  return typeof t === 'string' && (JOB_TYPES as readonly string[]).includes(t);
}
