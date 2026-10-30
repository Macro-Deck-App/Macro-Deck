import { appendFileSync } from 'node:fs';
import { join } from 'node:path';
import { test as base, expect } from '@playwright/test';
import { startHost } from './host.js';
import { HostApi } from './seed.js';

let currentHost = null;

// Secrets of every host in this run, for redact-playwright-artifacts.py. Kept beside the run
// directory, never inside an uploaded artifact folder.
function recordSecret(host) {
  const file = process.env.E2E_SECRETS_FILE ?? join(process.env.E2E_RUN_DIR ?? host.runDir, 'loopback-secrets.txt');
  appendFileSync(file, `${host.secret}\n`);
}

// One fresh host per spec file: started before its first test and stopped after its last, so a
// spec never depends on another one and two workers can run two specs side by side.
export function useIsolatedHost(name, { prepare, ...options } = {}) {
  base.beforeAll(async ({ playwright }, testInfo) => {
    testInfo.setTimeout(180_000);
    currentHost = await startHost(name, options);
    recordSecret(currentHost);
    if (prepare) {
      const request = await playwright.request.newContext();
      try {
        await prepare(new HostApi(currentHost, request), currentHost);
      } finally {
        await request.dispose();
      }
    }
  });

  base.afterAll(async ({}, testInfo) => {
    testInfo.setTimeout(60_000);
    const host = currentHost;
    currentHost = null;
    if (host) {
      await host.stop();
      console.log(`[${name}] host logs: ${host.runDir}`);
    }
  });
}

export const test = base.extend({
  host: async ({}, use) => {
    if (!currentHost) {
      throw new Error('This spec has no host: call useIsolatedHost() at the top of the file');
    }
    await use(currentHost);
  },

  api: async ({ host, request }, use) => {
    await use(new HostApi(host, request));
  },
});

export { expect };
