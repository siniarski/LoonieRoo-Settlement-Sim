/// <reference types="vite/client" />
// Guards architecture rules 2 and 3 by scanning the sim source.
// tsconfig.sim.json also blocks DOM types at typecheck time.

import { describe, expect, it } from 'vitest';

const simSources = import.meta.glob<string>('../src/sim/**/*.ts', {
  query: '?raw',
  import: 'default',
  eager: true,
});

const FORBIDDEN: [RegExp, string][] = [
  [/Math\.random\s*\(/, 'Math.random() (use rng.ts)'],
  [/Date\.now\s*\(|new Date\s*\(/, 'wall-clock time (use ticks)'],
  [/performance\.now\s*\(/, 'wall-clock time (use ticks)'],
  [/\b(window|document|localStorage|requestAnimationFrame)\b/, 'browser globals'],
  [/from\s+['"]\.\.\/(render|ui|debug)\//, 'imports from render/ui/debug'],
];

describe('sim architecture rules', () => {
  it('finds the sim sources', () => {
    expect(Object.keys(simSources).length).toBeGreaterThan(0);
  });

  for (const [path, source] of Object.entries(simSources)) {
    it(`${path} has no forbidden APIs`, () => {
      const violations = FORBIDDEN.filter(([pattern]) => pattern.test(source)).map(([, label]) => label);
      expect(violations).toEqual([]);
    });
  }
});
