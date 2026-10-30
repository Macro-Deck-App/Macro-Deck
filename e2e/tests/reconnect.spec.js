import { expect, test, useIsolatedHost } from './support/fixtures.js';
import { flow } from './support/seed.js';
import {
  connectWebClient,
  desktopTile,
  expectLivePush,
  openDesktopProfile,
  pressWebTile,
  webDeck,
  webTile,
} from './support/pages.js';

useIsolatedHost('reconnect', { prepare: api => api.completeOnboarding() });

// The outage length is the stimulus under test, not a wait for some state, so it is a plain timer.
async function holdOffline(context, milliseconds) {
  await context.setOffline(true);
  try {
    await new Promise(resolve => setTimeout(resolve, milliseconds));
  } finally {
    await context.setOffline(false);
  }
}

async function counterProfile(api, name, variable) {
  const profile = await api.profile(name, { columns: 3, rows: 2 });
  await api.variable(variable, { type: 'numeric', value: 0 });
  const button = await api.actionButton(profile.rootFolder.id, {
    label: 'press',
    flows: [flow.shortPress(flow.setVariable(variable, 1, 'add'))],
  });
  return { profile, button };
}

// E2E-CONN-02: losing the network neither signs the Web Client out nor leaves it on a dead socket.
for (const { title, outage } of [
  { title: 'a short outage', outage: 10_000 },
  { title: 'an outage longer than the 60 s stale window', outage: 90_000 },
]) {
  test(`the Web Client survives ${title} signed in and on a live socket`, async ({ browser, api, host }) => {
    test.setTimeout(outage + 90_000);
    const variable = `e2e_offline_${outage}`;
    const { profile, button } = await counterProfile(api, `E2E Offline ${outage}`, variable);
    const web = await connectWebClient(browser, host, api);
    await api.openProfileOnDevice(web.device.id, profile.id);
    await expect(webTile(web.page, button.id)).toBeVisible();

    await holdOffline(web.context, outage);

    await expect(webDeck(web.page)).toBeVisible();
    await expect(web.page.getByText('Sign in to continue')).toHaveCount(0);
    await expectLivePush(web.page, api, button.id, undefined, 30_000);
    await pressWebTile(web, button.id);
    await expect.poll(() => api.variableValue(variable)).toBe('1');
    await expect(web.page.getByText('Sign in to continue')).toHaveCount(0);
    await web.context.close();
  });
}

test('the desktop UI survives a short outage and keeps receiving pushes', async ({ page, api, host }) => {
  test.setTimeout(120_000);
  const { profile, button } = await counterProfile(api, 'E2E Desktop Offline', 'e2e_desktop_offline');
  await openDesktopProfile(page, host, profile.name);
  await expect(desktopTile(page, button.id)).toContainText('press');

  await holdOffline(page.context(), 10_000);

  await expect(page.getByRole('heading', { name: 'Welcome to Macro Deck' })).toHaveCount(0);
  await expectLivePush(page, api, button.id, desktopTile, 30_000);
});

// E2E-CONN-03: an action's configuration survives a dropped UI socket, and saving afterwards
// stores both the change made before and the change made after the drop.
test('an action editor keeps its draft across a dropped socket and saves both changes', async ({ page, api, host }) => {
  test.setTimeout(120_000);
  const profile = await api.profile('E2E Editor Reconnect', { columns: 3, rows: 2 });
  await api.variable('e2e_cfg', { type: 'numeric', value: 0 });
  const button = await api.actionButton(profile.rootFolder.id, {
    label: 'cfg',
    flows: [flow.shortPress(flow.setVariable('e2e_cfg', 1))],
  });

  const sockets = [];
  await page.routeWebSocket(/\/ws\/ui(\?|$)/, socket => {
    socket.connectToServer();
    sockets.push(socket);
  });
  await openDesktopProfile(page, host, profile.name);
  await desktopTile(page, button.id).click();
  await expect(page.getByText('Loading editor…')).toBeHidden({ timeout: 20_000 });
  await page.getByText('Variable: e2e_cfg').click();
  const value = page.getByTestId('set-variable-value').getByRole('spinbutton');
  await value.fill('7');
  await expect(page.getByRole('button', { name: 'Save', exact: true })).toBeVisible();

  const open = sockets.splice(0);
  expect(open.length).toBeGreaterThan(0);
  for (const socket of open) {
    await socket.close({ code: 4000, reason: 'e2e outage' });
  }
  await expect.poll(() => sockets.length, { message: 'the desktop UI opens a new socket' }).toBeGreaterThan(0);
  await expect(page.locator('app-splash')).toHaveCount(0, { timeout: 30_000 });

  await expect(value).toHaveValue('7');
  await page.getByTestId('set-variable-operation').click();
  await page.getByRole('option', { name: 'Add', exact: true }).click();
  await page.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Save', exact: true })).not.toBeVisible();

  const saved = JSON.parse((await api.findWidget(button.id)).parsed.flows);
  const parameters = Object.fromEntries(saved[0].children[0].parameters.map(item => [item.name, item.value]));
  expect(parameters).toMatchObject({ variable: 'e2e_cfg', operation: 'add', value: '7' });
});
