import { expect, test, useIsolatedHost } from './support/fixtures.js';
import { flow } from './support/seed.js';
import { desktopTile, openDesktopProfile, recordSocketFrames } from './support/pages.js';

useIsolatedHost('deletion', { prepare: api => api.completeOnboarding() });

// The configuration sessions the widget editor opened for the widget it shows.
function sessionsOpenedSince(frames, from) {
  const opened = [];
  for (const request of frames.sent.slice(from)) {
    if (request.kind !== 'request' || request.type !== 'OpenConfigUiSession') {
      continue;
    }
    const answer = frames.response(request)?.payload;
    if (answer?.accepted && answer.sessionId) {
      opened.push(answer.sessionId);
    }
  }
  return opened;
}

// Sessions the host ended because their widget was deleted. The page also closes the session when
// the editor goes away, in either order, but that close carries its own reason.
function closedByHost(frames) {
  return frames.received
    .filter(message => message.type === 'UiSessionClosedEvent'
      && message.payload?.reason === 'The widget was deleted.')
    .map(message => message.payload?.sessionId);
}

async function openEditorOn(page, host, profile, widgetId) {
  const frames = recordSocketFrames(page);
  await openDesktopProfile(page, host, profile.name);
  const before = frames.sent.length;
  await desktopTile(page, widgetId).click();
  await expect(page.getByText('Loading editor…')).toBeHidden({ timeout: 20_000 });
  let sessions = [];
  await expect.poll(() => {
    sessions = sessionsOpenedSince(frames, before);
    return sessions.length;
  }, { message: 'the editor opens a configuration session for the widget' }).toBeGreaterThan(0);
  return { frames, sessions };
}

async function seedButtons(api, name) {
  const profile = await api.profile(name, { columns: 4, rows: 2 });
  const buttons = [];
  for (let x = 0; x < 3; x += 1) {
    buttons.push(await api.actionButton(profile.rootFolder.id, {
      x,
      label: `delete me ${x}`,
      flows: [flow.shortPress(flow.setVariable('e2e_unused', x))],
    }));
  }
  return { profile, buttons };
}

// E2E-DEL-01: an editor open in one desktop window on a widget another window deletes is told its
// session ended. The single delete is the reference for what a batch delete has to do as well.
test('deleting one widget ends the editor session another desktop window holds on it', async ({ page, api, host }) => {
  const { profile, buttons } = await seedButtons(api, 'E2E Delete One');
  const editor = await openEditorOn(page, host, profile, buttons[0].id);

  await api.delete(`/api/widgets/${buttons[0].id}?folderId=${profile.rootFolder.id}`);

  await expect.poll(() => closedByHost(editor.frames).filter(id => editor.sessions.includes(id)).length)
    .toBeGreaterThan(0);
});

test('deleting several widgets at once ends the editor session another desktop window holds on one of them', async ({ page, api, host }) => {
  const { profile, buttons } = await seedButtons(api, 'E2E Delete Many');
  const editor = await openEditorOn(page, host, profile, buttons[1].id);

  await api.post('/api/widgets/batch/delete', {
    folderId: profile.rootFolder.id,
    ids: buttons.map(button => button.id),
  });
  await expect.poll(async () => (await api.rootFolder(profile.id)).widgets.length).toBe(0);

  await expect.poll(() => closedByHost(editor.frames).filter(id => editor.sessions.includes(id)).length)
    .toBeGreaterThan(0);
});

test('an editor left open on a deleted widget says so and leaves without asking to save', async ({ page, api, host }) => {
  const { profile, buttons } = await seedButtons(api, 'E2E Delete Editor');
  await openEditorOn(page, host, profile, buttons[0].id);

  await api.delete(`/api/widgets/${buttons[0].id}?folderId=${profile.rootFolder.id}`);

  await expect(page.getByText('This widget no longer exists.')).toBeVisible();
  await page.getByRole('button', { name: 'Back to deck' }).first().click();
  await expect(desktopTile(page, buttons[1].id)).toBeVisible();
});

test('a single delete the host refuses puts the widget back and shows an error', async ({ page, api, host }) => {
  const { profile, buttons } = await seedButtons(api, 'E2E Delete Refused');
  await openDesktopProfile(page, host, profile.name);
  await page.route(`**/api/widgets/${buttons[0].id}?*`, route => (route.request().method() === 'DELETE'
    ? route.fulfill({ status: 500, body: '{}' })
    : route.continue()));

  await desktopTile(page, buttons[0].id).click({ button: 'right' });
  await page.locator('.menu-item').filter({ hasText: /^\s*Delete\s*$/ }).click();
  await page.locator('shared-confirmation-modal').getByRole('button', { name: 'Delete' }).click();

  await expect(page.locator('shared-error-banner')).toBeVisible();
  await expect(desktopTile(page, buttons[0].id)).toBeVisible();
});
