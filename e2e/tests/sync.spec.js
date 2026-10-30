import { expect, test, useIsolatedHost } from './support/fixtures.js';
import { flow } from './support/seed.js';
import {
  clickProfileAction,
  connectWebClient,
  countLoads,
  desktopTile,
  openDesktopProfile,
  pressWebTile,
  webDeck,
  webTile,
} from './support/pages.js';

useIsolatedHost('sync', { prepare: api => api.completeOnboarding() });

function webGridCellCount(page) {
  return page.locator('.wc-deck .deck-grid-cell, .wc-deck .deck-grid-tile').count();
}

// E2E-SYNC-01: what the desktop UI or the REST API changes reaches a connected Web Client over its
// socket, without the page ever reloading.
test('desktop edits and variable changes appear live in the Web Client', async ({ page, browser, api, host }) => {
  const profile = await api.profile('E2E Sync', { columns: 3, rows: 2 });
  const button = await api.actionButton(profile.rootFolder.id, { label: 'Alpha' });
  const web = await connectWebClient(browser, host, api);
  await api.openProfileOnDevice(web.device.id, profile.id);
  await expect(webTile(web.page, button.id)).toHaveText('Alpha');
  await expect.poll(() => webGridCellCount(web.page)).toBe(6);
  const webLoads = countLoads(web.page);

  await openDesktopProfile(page, host, profile.name);
  await desktopTile(page, button.id).click();
  await expect(page.getByRole('heading', { name: 'Action Button' })).toBeVisible();
  await page.getByRole('textbox', { name: 'Label', exact: true }).fill('Beta');
  await page.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Save', exact: true })).not.toBeVisible();
  await expect(webTile(web.page, button.id)).toHaveText('Beta', { timeout: 5_000 });
  await page.getByRole('button', { name: 'Back to deck' }).click();

  await clickProfileAction(page, profile.name, 'Edit');
  const editModal = page.getByRole('dialog', { name: 'Edit Profile' });
  const sliders = editModal.getByRole('slider');
  await sliders.nth(0).fill('5');
  await sliders.nth(1).fill('3');
  await editModal.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByText('5 × 3 Grid')).toBeVisible();
  await expect.poll(() => webGridCellCount(web.page), { timeout: 5_000 }).toBe(15);

  await api.updateWidgetData(button.id, { label: '[{{ vars.e2e_foo }}][{{ vars.e2e_bar }}]' });
  await expect(webTile(web.page, button.id)).toHaveText('[][]', { timeout: 5_000 });

  await api.variable('e2e_foo', { value: '5' });
  await expect(webTile(web.page, button.id)).toHaveText('[5][]', { timeout: 5_000 });

  const foo = await api.findVariable('e2e_foo');
  await api.put('/api/variables', { id: foo.id, name: 'e2e_bar' });
  await expect(webTile(web.page, button.id)).toHaveText('[][5]', { timeout: 5_000 });

  await api.deleteVariable('e2e_bar');
  await expect(webTile(web.page, button.id)).toHaveText('[][]', { timeout: 5_000 });

  expect(webLoads.loads).toBe(0);
  await web.context.close();
});

// Records every text a tile shows from the moment it is inserted, so a stale first paint is caught
// even when the right text replaces it a frame later.
async function recordTileTexts(page, widgetId) {
  await page.evaluate(id => {
    const seen = [];
    window.__e2eTileTexts = seen;
    const record = () => {
      const tile = document.querySelector(`.wc-deck [data-widget-id="${id}"]`);
      const text = tile ? tile.textContent.trim() : null;
      if (text && seen[seen.length - 1] !== text) {
        seen.push(text);
      }
    };
    const observer = new MutationObserver(record);
    observer.observe(document.body, { childList: true, subtree: true, characterData: true });
    record();
  }, widgetId);
  return () => page.evaluate(() => window.__e2eTileTexts);
}

// E2E-SYNC-02: a button bound to a variable shows its current state on the very first paint after
// navigating back into its folder, not the state it had when the folder was left.
test('a variable-bound button shows its current state on the first paint after navigating back', async ({ browser, api, host }) => {
  const profile = await api.profile('E2E Sync Back', { columns: 3, rows: 2 });
  await api.variable('e2e_mode', { value: 'a' });
  const sub = await api.folder(profile.id, 'Sub', profile.rootFolder.id);
  const open = await api.actionButton(profile.rootFolder.id, {
    label: 'Open Sub',
    flows: [flow.shortPress(flow.changeFolder(sub.id))],
  });
  const back = await api.actionButton(sub.id, { x: 0, label: 'Back', flows: [flow.shortPress(flow.goBack())] });
  const bound = await api.actionButton(sub.id, {
    x: 1,
    label: 'bound',
    states: [{ id: 'a', label: 'state A' }, { id: 'b', label: 'state B' }],
    stateMapping: { rules: [{ id: 'rule-b', stateId: 'b', when: flow.equals('e2e_mode', 'b') }], fallbackStateId: 'a' },
  });

  const web = await connectWebClient(browser, host, api);
  await api.openProfileOnDevice(web.device.id, profile.id);
  await pressWebTile(web, open.id);
  await expect(webTile(web.page, bound.id)).toHaveText('state A');

  await pressWebTile(web, back.id);
  await expect(webTile(web.page, open.id)).toBeVisible();
  await expect(webTile(web.page, bound.id)).toHaveCount(0);
  // A second device that stays in the folder proves the host already shows state B, so whatever the
  // first device paints on its way back in is its own doing.
  const witness = await connectWebClient(browser, host, api);
  await api.openProfileOnDevice(witness.device.id, profile.id);
  await pressWebTile(witness, open.id);
  await expect(webTile(witness.page, bound.id)).toHaveText('state A');
  await api.setVariable('e2e_mode', 'b');
  await expect(webTile(witness.page, bound.id)).toHaveText('state B');

  const texts = await recordTileTexts(web.page, bound.id);
  await webTile(web.page, open.id).click();
  await expect(webTile(web.page, bound.id)).toHaveText('state B');
  expect(await texts()).toEqual(['state B']);
  await expect(webDeck(web.page)).toBeVisible();
  await witness.context.close();
  await web.context.close();
});
