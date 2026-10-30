import { expect, test, useIsolatedHost } from './support/fixtures.js';
import { flow } from './support/seed.js';
import {
  connectWebClient,
  countLoads,
  desktopTile,
  openDesktopProfile,
  pressWebTile,
  webTile,
} from './support/pages.js';

// E2E-ACT-01: a press in the Web Client runs the button's flow on the host, and what the flow
// changed reaches both UIs over their live sockets.
useIsolatedHost('actions', { prepare: api => api.completeOnboarding() });

test('a press runs the flow once and its variable, label and state reach the Web Client and the desktop UI', async ({ page, browser, api, host }) => {
  const profile = await api.profile('E2E Actions', { columns: 3, rows: 2 });
  await api.variable('e2e_counter', { type: 'numeric', value: 0 });
  const button = await api.actionButton(profile.rootFolder.id, {
    label: '{{ vars.e2e_counter }}',
    flows: [flow.shortPress(flow.setVariable('e2e_counter', 1, 'add'))],
    states: [
      { id: 'off', label: 'off {{ vars.e2e_counter }}' },
      { id: 'on', label: 'on {{ vars.e2e_counter }}' },
    ],
  });

  const web = await connectWebClient(browser, host, api);
  await api.openProfileOnDevice(web.device.id, profile.id);
  await expect(webTile(web.page, button.id)).toHaveText('off 0');
  await openDesktopProfile(page, host, profile.name);
  await expect(desktopTile(page, button.id)).toContainText('off 0');
  const desktopLoads = countLoads(page);
  const webLoads = countLoads(web.page);

  for (let press = 1; press <= 3; press += 1) {
    await pressWebTile(web, button.id);
    await expect(webTile(web.page, button.id)).toHaveText(`${press % 2 === 1 ? 'on' : 'off'} ${press}`);
  }

  await expect.poll(() => api.variableValue('e2e_counter')).toBe('3');
  expect((await api.findWidget(button.id)).parsed.activeStateId).toBe('on');
  const presses = web.frames.requests('SendUiEvent').filter(request => request.payload?.[0]?.name === 'press');
  expect(presses).toHaveLength(3);
  await expect(webTile(web.page, button.id)).toHaveText('on 3');
  await expect(desktopTile(page, button.id)).toContainText('on 3');

  expect(desktopLoads.loads).toBe(0);
  expect(webLoads.loads).toBe(0);
  await web.context.close();
});

test('a slow flow finishes on its own while the other buttons keep answering', async ({ browser, api, host }) => {
  const profile = await api.profile('E2E Slow Flow', { columns: 3, rows: 2 });
  await api.variable('e2e_fast', { type: 'numeric', value: 0 });
  await api.variable('e2e_slow', { type: 'numeric', value: 0 });
  const fast = await api.actionButton(profile.rootFolder.id, {
    x: 0,
    label: 'fast {{ vars.e2e_fast }}',
    flows: [flow.shortPress(flow.setVariable('e2e_fast', 1, 'add'))],
  });
  const slow = await api.actionButton(profile.rootFolder.id, {
    x: 1,
    label: 'slow {{ vars.e2e_slow }}',
    flows: [flow.shortPress(flow.delay(7_000), flow.setVariable('e2e_slow', 1))],
  });

  const web = await connectWebClient(browser, host, api);
  await api.openProfileOnDevice(web.device.id, profile.id);
  await expect(webTile(web.page, slow.id)).toHaveText('slow 0');
  await expect(webTile(web.page, fast.id)).toHaveText('fast 0');

  const slowPressed = Date.now();
  await pressWebTile(web, slow.id);
  await pressWebTile(web, fast.id);
  await expect(webTile(web.page, fast.id)).toHaveText('fast 1', { timeout: 1_000 });
  // The fast button answered while the slow flow was still waiting in its delay.
  expect(Date.now() - slowPressed).toBeLessThan(7_000);
  expect(await api.variableValue('e2e_slow')).toBe('0');

  await expect(webTile(web.page, slow.id)).toHaveText('slow 1', { timeout: 15_000 });
  expect(Date.now() - slowPressed).toBeGreaterThanOrEqual(7_000);
  expect(await api.variableValue('e2e_fast')).toBe('1');
  await web.context.close();
});
