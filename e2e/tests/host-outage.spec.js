import { expect, test, useIsolatedHost } from './support/fixtures.js';
import { flow } from './support/seed.js';
import {
  connectWebClient,
  countLoads,
  desktopTile,
  expectLivePush,
  openDesktopProfile,
  pressWebTile,
  selectDesktopProfile,
  webDeck,
} from './support/pages.js';

useIsolatedHost('host-outage', { prepare: api => api.completeOnboarding() });

// The desktop UI reloads itself once for a new host session, by design.
test('both UIs recover from a host that was stopped for 70 s, the Web Client without a reload', async ({ page, browser, api, host }) => {
  test.setTimeout(240_000);
  const profile = await api.profile('E2E Host Outage', { columns: 3, rows: 2 });
  await api.variable('e2e_outage', { type: 'numeric', value: 0 });
  const button = await api.actionButton(profile.rootFolder.id, {
    label: 'outage',
    flows: [flow.shortPress(flow.setVariable('e2e_outage', 1, 'add'))],
  });

  const desktopLoads = countLoads(page);
  await openDesktopProfile(page, host, profile.name);
  await expect(desktopTile(page, button.id)).toContainText('outage');

  const web = await connectWebClient(browser, host, api);
  const webLoads = countLoads(web.page);
  await api.openProfileOnDevice(web.device.id, profile.id);
  await pressWebTile(web, button.id);
  await expect.poll(() => api.variableValue('e2e_outage')).toBe('1');
  const loadsBefore = { desktop: desktopLoads.loads, web: webLoads.loads };

  await host.stop();
  await new Promise(resolve => setTimeout(resolve, 70_000));
  const desktopReloaded = page.waitForEvent('load', { timeout: 60_000 });
  await host.start();
  await desktopReloaded;
  await selectDesktopProfile(page, profile.name);

  await expect(webDeck(web.page)).toBeVisible();
  await expect(web.page.getByText('Sign in to continue')).toHaveCount(0);
  await expect(page.getByRole('heading', { name: 'Welcome to Macro Deck' })).toHaveCount(0);
  await expectLivePush(page, api, button.id, desktopTile, 60_000);
  await expectLivePush(web.page, api, button.id, undefined, 60_000);
  await pressWebTile(web, button.id);
  await expect.poll(() => api.variableValue('e2e_outage')).toBe('2');
  expect({ desktop: desktopLoads.loads, web: webLoads.loads })
    .toEqual({ desktop: loadsBefore.desktop + 1, web: loadsBefore.web });
  await web.context.close();
});
