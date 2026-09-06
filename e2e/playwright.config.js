import { defineConfig } from '@playwright/test';

export const LOOPBACK_URL = process.env.MACRO_DECK_LOOPBACK_URL ?? 'http://127.0.0.1:5191';
export const PUBLIC_URL = process.env.MACRO_DECK_PUBLIC_URL ?? 'http://127.0.0.1:8192';

export default defineConfig({
  testDir: './tests',
  timeout: 45_000,
  expect: { timeout: 10_000 },
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: [
    ['list'],
    ['html', { outputFolder: 'playwright-report', open: 'never' }],
  ],
  use: {
    baseURL: LOOPBACK_URL,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
    viewport: { width: 1440, height: 1000 },
  },
  outputDir: 'test-results',
});
