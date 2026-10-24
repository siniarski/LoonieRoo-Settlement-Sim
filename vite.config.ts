import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    include: ['tests/**/*.test.ts'],
    environment: 'node',
    // The persistent module cache doesn't refresh import.meta.glob when files
    // are added, so tests/architecture.test.ts would miss new sim files.
    fsModuleCache: false,
  },
});
