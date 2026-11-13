import { readFileSync } from 'node:fs';
import { basename } from 'node:path';
import { expect, test, useIsolatedHost } from './support/fixtures.js';
import { FIXTURE_PLUGIN, buildFixturePlugin, pluginRuntime, waitUntilPluginServes } from './support/plugin.js';
import { flow } from './support/seed.js';
import { connectWebClient, openDesktop, pressWebTile, webTile } from './support/pages.js';

useIsolatedHost('plugins', { prepare: api => api.completeOnboarding() });

let packagePath;

test.beforeAll(async ({}, testInfo) => {
  testInfo.setTimeout(600_000);
  packagePath = await buildFixturePlugin();
});

const unavailableText = `Provided by ${FIXTURE_PLUGIN.name}, currently unavailable`;
const unavailable = /currently unavailable/;

async function installOverRest(api) {
  const response = await api.request.post(`${api.host.loopbackUrl}/api/plugin-installation/install?allowUnsigned=true&force=true`, {
    headers: api.headers(),
    multipart: {
      file: { name: basename(packagePath), mimeType: 'application/zip', buffer: readFileSync(packagePath) },
    },
  });
  expect(response.ok(), await response.text()).toBe(true);
}

async function ensurePluginRunning(api) {
  const runtime = await pluginRuntime(api);
  if (!runtime) {
    await installOverRest(api);
  } else if (runtime.state !== 'running') {
    await api.post(`/api/plugin-runtime/${FIXTURE_PLUGIN.id}/start`, {});
  }
  await waitUntilPluginServes(api);
}

// E2E-PLG-04. An Action Button is never unavailable itself, so the stopped plugin is checked on the tile of
// its own widget type.
test('a local plugin installed in the desktop UI serves its action, variable and widget until it stops', async ({ page, browser, api, host }) => {
  test.setTimeout(300_000);
  await openDesktop(page, host);
  await page.getByRole('link', { name: 'Integrations' }).click();
  await page.getByRole('button', { name: 'Add an integration' }).click();
  const chooser = page.waitForEvent('filechooser');
  await page.getByRole('button', { name: 'Install from file…' }).click();
  await (await chooser).setFiles(packagePath);
  const dialog = page.getByRole('dialog', { name: 'Install this plugin?' });
  await expect(dialog).toBeVisible();
  const install = dialog.getByRole('button', { name: 'Install', exact: true });
  await expect(install).toBeDisabled();
  await dialog.getByText(/I understand this package is unsigned/).click();
  await install.click();
  await expect(page.getByText(`Installed ${FIXTURE_PLUGIN.id} 1.0.0`)).toBeVisible({ timeout: 60_000 });
  await waitUntilPluginServes(api);

  const profile = await api.profile('E2E Plugin', { columns: 3, rows: 2 });
  const button = await api.actionButton(profile.rootFolder.id, {
    label: `text {{ vars.${FIXTURE_PLUGIN.textVariable} }}`,
    flows: [flow.shortPress(flow.action(FIXTURE_PLUGIN.id, FIXTURE_PLUGIN.setTextAction, { text: 'from the plugin' }))],
  });
  const tile = await api.widget(profile.rootFolder.id, { type: FIXTURE_PLUGIN.tileType, x: 1 });

  const web = await connectWebClient(browser, host, api);
  await api.openProfileOnDevice(web.device.id, profile.id);
  await expect(webTile(web.page, tile.id)).toContainText(FIXTURE_PLUGIN.tileText);

  const { locked } = await api.get('/api/system/lock-state');
  await pressWebTile(web, button.id);
  if (locked) {
    test.info().annotations.push({ type: 'host-locked', description: 'The press was refused, so the variable was not checked.' });
    await expect.poll(() => web.frames.lockRefusalsFor(button.id).length).toBeGreaterThan(0);
  } else {
    await expect(webTile(web.page, button.id)).toContainText('text from the plugin', { timeout: 30_000 });
  }

  await api.post(`/api/plugin-runtime/${FIXTURE_PLUGIN.id}/stop`, {});
  await expect.poll(async () => (await pluginRuntime(api))?.state).not.toBe('running');
  await expect(webTile(web.page, tile.id)).toContainText(unavailable, { timeout: 30_000 });
  await web.context.close();
});

test('the tile of a stopped plugin names the plugin by its display name', async ({ browser, api, host }) => {
  await ensurePluginRunning(api);
  const profile = await api.profile('E2E Plugin Name', { columns: 3, rows: 2 });
  const tile = await api.widget(profile.rootFolder.id, { type: FIXTURE_PLUGIN.tileType });
  const web = await connectWebClient(browser, host, api);
  await api.openProfileOnDevice(web.device.id, profile.id);
  await expect(webTile(web.page, tile.id)).toContainText(FIXTURE_PLUGIN.tileText);

  await api.post(`/api/plugin-runtime/${FIXTURE_PLUGIN.id}/stop`, {});
  await expect(webTile(web.page, tile.id)).toContainText(unavailableText, { timeout: 30_000 });
  await web.context.close();
});

async function seedTileFolder(api, name, columns, rows) {
  const profile = await api.profile(name, { columns, rows });
  const tiles = [];
  for (let y = 0; y < rows; y += 1) {
    for (let x = 0; x < columns; x += 1) {
      tiles.push(await api.widget(profile.rootFolder.id, { type: FIXTURE_PLUGIN.tileType, x, y }));
    }
  }
  return { profile, tiles };
}

async function expectEveryTilePainted(client, tiles) {
  const painted = client.page.locator('.wc-deck [data-widget-id]').filter({ hasText: FIXTURE_PLUGIN.tileText });
  await expect(painted).toHaveCount(tiles.length, { timeout: 60_000 });
  await expect(client.page.locator('.wc-deck').getByText(unavailable)).toHaveCount(0);
}

// E2E-MULTI-01. The per-provider widget session limit (128) has to count per viewing device. Six devices on
// 24 tiles each go past it in total, while each device stays under the plugin's 32 concurrent invocations.
test('six Web Clients on a folder of 24 plugin tiles each paint every tile', async ({ browser, api, host }) => {
  test.setTimeout(300_000);
  await ensurePluginRunning(api);
  const { profile, tiles } = await seedTileFolder(api, 'E2E Many Plugin Tiles', 6, 4);

  const clients = [];
  for (let index = 0; index < 6; index += 1) {
    const client = await connectWebClient(browser, host, api);
    await api.openProfileOnDevice(client.device.id, profile.id);
    await expectEveryTilePainted(client, tiles);
    clients.push(client);
  }
  for (const client of clients) {
    await expectEveryTilePainted(client, tiles);
    await client.context.close();
  }
});

// Host bug: PluginCapabilityInvoker rejects a call past 32 concurrent invocations as retryable, but nothing
// retries a UI session open, so a burst of 45 tiles leaves some blank for good. Unskip when opens retry.
test.fixme('three Web Clients on a folder of 45 plugin tiles each paint every tile', async ({ browser, api, host }) => {
  test.setTimeout(300_000);
  await ensurePluginRunning(api);
  const { profile, tiles } = await seedTileFolder(api, 'E2E Plugin Tile Burst', 9, 5);

  const clients = [];
  for (let index = 0; index < 3; index += 1) {
    const client = await connectWebClient(browser, host, api);
    await api.openProfileOnDevice(client.device.id, profile.id);
    clients.push(client);
  }
  for (const client of clients) {
    await expectEveryTilePainted(client, tiles);
    await client.context.close();
  }
});
