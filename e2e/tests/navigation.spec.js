import { expect, test, useIsolatedHost } from './support/fixtures.js';
import { flow } from './support/seed.js';
import { connectWebClient, pressWebTile, webDeck, webTile } from './support/pages.js';

useIsolatedHost('navigation', { prepare: api => api.completeOnboarding() });

function webGridCellCount(page) {
  return page.locator('.wc-deck .deck-grid-cell, .wc-deck .deck-grid-tile').count();
}

function errorToasts(page) {
  return page.locator('.wc-toast-error');
}

// The folder the Web Client last told the host it shows.
function reportedFolder(client) {
  const reports = client.frames.requests('ReportFolderChanged');
  return reports[reports.length - 1]?.payload?.folderId ?? null;
}

// E2E-NAV-01: folders can be created, renamed and walked in the Web Client, and deleting the folder
// or the profile a Web Client shows moves it somewhere that still exists, without an error.
test('folder navigation survives renaming and deleting what the Web Client shows', async ({ browser, api, host }) => {
  const profile = await api.profile('E2E Navigation', { columns: 3, rows: 2 });
  const sub = await api.folder(profile.id, 'Sub', profile.rootFolder.id);
  const nested = await api.folder(profile.id, 'Nested', sub.id);
  const openSub = await api.actionButton(profile.rootFolder.id, {
    label: 'Open Sub',
    flows: [flow.shortPress(flow.changeFolder(sub.id))],
  });
  const back = await api.actionButton(sub.id, { x: 0, label: 'Back', flows: [flow.shortPress(flow.goBack())] });
  const openNested = await api.actionButton(sub.id, {
    x: 1,
    label: 'Open Nested',
    flows: [flow.shortPress(flow.changeFolder(nested.id))],
  });
  await api.actionButton(nested.id, { label: 'Deep' });

  await api.put('/api/folders', { id: sub.id, profileId: profile.id, name: 'Sub Renamed', parentId: profile.rootFolder.id });
  await expect.poll(async () => (await api.folders(profile.id)).find(folder => folder.id === sub.id)?.name)
    .toBe('Sub Renamed');

  const web = await connectWebClient(browser, host, api);
  await api.openProfileOnDevice(web.device.id, profile.id);
  await pressWebTile(web, openSub.id);
  await expect(webTile(web.page, openNested.id)).toBeVisible();
  await pressWebTile(web, back.id);
  await expect(webTile(web.page, openSub.id)).toBeVisible();
  await pressWebTile(web, openSub.id);
  await expect(webTile(web.page, openNested.id)).toBeVisible();
  await expect.poll(() => reportedFolder(web)).toBe(sub.id);

  await api.delete(`/api/folders/${sub.id}`);
  await expect(webTile(web.page, openSub.id)).toBeVisible({ timeout: 5_000 });
  await expect(webTile(web.page, openNested.id)).toHaveCount(0);
  await expect(errorToasts(web.page)).toHaveCount(0);
  const remaining = (await api.folders(profile.id)).map(folder => folder.id);
  expect(remaining).not.toContain(sub.id);
  expect(remaining).not.toContain(nested.id);

  await web.page.reload();
  await expect(webDeck(web.page)).toBeVisible();
  await expect(webTile(web.page, openNested.id)).toHaveCount(0);

  await api.openProfileOnDevice(web.device.id, profile.id);
  await expect(webTile(web.page, openSub.id)).toBeVisible();
  await api.delete(`/api/profiles/${profile.id}`);
  await expect(webTile(web.page, openSub.id)).toHaveCount(0, { timeout: 5_000 });
  await expect(webDeck(web.page)).toBeVisible();
  await expect(web.page.getByText('Sign in to continue')).toHaveCount(0);
  await expect(errorToasts(web.page)).toHaveCount(0);
  const existingFolders = (await api.folders()).map(folder => folder.id);
  await expect.poll(() => existingFolders.includes(reportedFolder(web))).toBe(true);
  await web.context.close();
});

// E2E-NAV-02: Change Profile lands on the target profile's own grid and start folder.
test('Change Profile switches to the grid and start folder of the target profile', async ({ browser, api, host }) => {
  const small = await api.profile('E2E Small Grid', { columns: 3, rows: 2 });
  const large = await api.profile('E2E Large Grid', { columns: 5, rows: 3 });
  const toLarge = await api.actionButton(small.rootFolder.id, {
    label: 'To Large',
    flows: [flow.shortPress(flow.changeProfile(large.id))],
  });
  const toSmall = await api.actionButton(large.rootFolder.id, {
    x: 4,
    y: 2,
    label: 'To Small',
    flows: [flow.shortPress(flow.changeProfile(small.id))],
  });

  const web = await connectWebClient(browser, host, api);
  await api.openProfileOnDevice(web.device.id, small.id);
  await expect(webTile(web.page, toLarge.id)).toBeVisible();
  await expect.poll(() => webGridCellCount(web.page)).toBe(6);

  await pressWebTile(web, toLarge.id);
  await expect(webTile(web.page, toSmall.id)).toBeVisible();
  await expect.poll(() => webGridCellCount(web.page)).toBe(15);
  await expect.poll(() => reportedFolder(web)).toBe(large.rootFolder.id);

  await pressWebTile(web, toSmall.id);
  await expect(webTile(web.page, toLarge.id)).toBeVisible();
  await expect.poll(() => webGridCellCount(web.page)).toBe(6);
  await expect.poll(() => reportedFolder(web)).toBe(small.rootFolder.id);
  await expect(errorToasts(web.page)).toHaveCount(0);
  await web.context.close();
});
