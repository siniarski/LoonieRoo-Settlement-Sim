// Job types and priorities. Kept separate from jobs.ts so survivors.ts can
// use them without an import cycle.

export type JobType = 'build' | 'haul' | 'clean';

/** Also the tie-break order when two job types share a priority. */
export const JOB_TYPES: readonly JobType[] = ['build', 'haul', 'clean'];

/** 1 is highest, 4 lowest, 0 means the survivor won't do this job type. */
export type Priority = 0 | 1 | 2 | 3 | 4;

export type Priorities = Record<JobType, Priority>;

export const DEFAULT_PRIORITIES: Priorities = { build: 3, haul: 3, clean: 3 };

export function isPriority(n: unknown): n is Priority {
  return n === 0 || n === 1 || n === 2 || n === 3 || n === 4;
}

export function isJobType(t: unknown): t is JobType {
  return typeof t === 'string' && (JOB_TYPES as readonly string[]).includes(t);
}
