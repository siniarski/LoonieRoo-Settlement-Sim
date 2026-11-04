// The harness (harness/*.harness.ts): balance players, a benchmark, a profiler. Not part of npm test.
// Run one: npm run harness -- <name>   e.g. npm run harness -- solo
import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    include: ['harness/**/*.harness.ts'],
    environment: 'node',
    fsModuleCache: false,
    testTimeout: 3_600_000,
  },
});
