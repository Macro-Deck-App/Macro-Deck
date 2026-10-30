import { readdirSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test, useIsolatedHost } from './support/fixtures.js';
import { connectWebClient, expectLivePush, webDeck } from './support/pages.js';

useIsolatedHost('backup', { prepare: api => api.completeOnboarding() });

function failedLeftovers(directory) {
  const found = [];
  for (const entry of readdirSync(directory, { withFileTypes: true })) {
    const path = join(directory, entry.name);
    if (entry.name.includes('.failed-')) {
      found.push(path);
    }
    if (entry.isDirectory()) {
      found.push(...failedLeftovers(path));
    }
  }
  return found;
}

async function createBackup(api) {
  const created = await api.post('/api/backups', { note: 'e2e' });
  expect(created.success).toBe(true);
  await expect.poll(async () => (await api.get('/api/backups/status')).stage, { timeout: 60_000 })
    .toMatch(/^(Idle|Completed)$/);
  return created.backup;
}

async function waitUntilOnline(api, deviceId) {
  await expect.poll(async () => (await api.devices()).find(device => device.id === deviceId)?.online ?? false, {
    timeout: 60_000,
    message: 'the paired Web Client reconnects on its own',
  }).toBe(true);
}

async function restore(api, host, backupId, components) {
  const prepared = await api.post('/api/backups/restore/prepare', { backupId, components });
  expect(prepared, JSON.stringify(prepared.error)).toMatchObject({ success: true });
  const starts = host.starts;
  const committed = await api.post('/api/backups/restore/commit', { restoreId: prepared.restoreId });
  expect(committed).toMatchObject({ success: true, restartRequested: true });
  await host.waitForRestart(starts);
}

// E2E-BAK-01: a restore and its restart bring deleted data back, a partial restore keeps the account
// and paired devices, and neither leaves a failed restore behind in the data directory.
test('a full restore brings deleted data back and keeps the paired Web Client signed in', async ({ browser, api, host }) => {
  test.setTimeout(240_000);
  // A profile of its own to keep, so deleting the backed-up one is never refused as the last one.
  await api.profile('E2E Keeper');
  const profile = await api.profile('E2E Backed Up', { columns: 3, rows: 2 });
  const button = await api.actionButton(profile.rootFolder.id, { label: 'backed up' });
  await api.variable('e2e_backed_up', { value: 'keep me' });
  const web = await connectWebClient(browser, host, api);
  await api.openProfileOnDevice(web.device.id, profile.id);

  const backup = await createBackup(api);
  await api.delete(`/api/profiles/${profile.id}`);
  await api.deleteVariable('e2e_backed_up');
  expect((await api.profiles()).some(item => item.id === profile.id)).toBe(false);

  const catalog = (await api.get('/api/backups')).backups.find(item => item.id === backup.id);
  await restore(api, host, backup.id, catalog.components);

  await expect.poll(async () => (await api.profiles()).some(item => item.id === profile.id), { timeout: 30_000 })
    .toBe(true);
  expect(await api.variableValue('e2e_backed_up')).toBe('keep me');
  expect(failedLeftovers(host.dataDir)).toEqual([]);

  await waitUntilOnline(api, web.device.id);
  await expect(web.page.getByText('Sign in to continue')).toHaveCount(0);
  await api.openProfileOnDevice(web.device.id, profile.id);
  await expect(webDeck(web.page)).toBeVisible();
  await expectLivePush(web.page, api, button.id, undefined, 30_000);
  await web.context.close();
});

test('restoring only integrations keeps the account and the paired Web Client', async ({ browser, api, host }) => {
  test.setTimeout(240_000);
  const profile = await api.profile('E2E Partial Restore', { columns: 3, rows: 2 });
  const button = await api.actionButton(profile.rootFolder.id, { label: 'partial' });
  const backup = await createBackup(api);
  const web = await connectWebClient(browser, host, api);
  await api.openProfileOnDevice(web.device.id, profile.id);

  await restore(api, host, backup.id, ['Integrations']);
  expect(failedLeftovers(host.dataDir)).toEqual([]);

  await waitUntilOnline(api, web.device.id);
  await expect(web.page.getByText('Sign in to continue')).toHaveCount(0);
  await api.openProfileOnDevice(web.device.id, profile.id);
  await expectLivePush(web.page, api, button.id, undefined, 30_000);

  const login = await api.request.post(`${host.publicUrl}/api/auth/login`, {
    data: { username: 'e2e-user', password: 'macro-deck-e2e-password', scope: 'client' },
  });
  expect(login.ok()).toBe(true);
  await web.context.close();
});
