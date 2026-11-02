import { defineConfig } from 'vitest/config';

// Bound jsdom workers so the full regression suite can run alongside local services.
export default defineConfig({ test: { maxWorkers: 2 } });
