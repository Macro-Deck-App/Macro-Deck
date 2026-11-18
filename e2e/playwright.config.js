import { defineConfig } from '@playwright/test';

// Every spec file starts its own staged host (tests/support/fixtures.js), so workers can run spec
// files side by side; CI keeps one because the runner has two cores.
const workers = Number.parseInt(process.env.E2E_WORKERS ?? '1', 10);

export default defineConfig({
  testDir: './tests',
  timeout: 60_000,
  expect: { timeout: 10_000 },
  fullyParallel: false,
  workers,
  // One retry in CI, so a flake no longer blocks a release; ci/scripts/flaky-tests.mjs reports every
  // test that needed it, from results.json, and tracks them across main runs.
  retries: process.env.CI ? 1 : 0,
  reporter: [
    ['list'],
    ['html', { outputFolder: 'playwright-report', open: 'never' }],
    ['junit', { outputFile: 'test-results/junit.xml' }],
    ['json', { outputFile: 'test-results/results.json' }],
  ],
  use: {
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
    viewport: { width: 1440, height: 1000 },
  },
  outputDir: 'test-results',
});
