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

// Sessions the host ended on its own: a close the page asked for itself proves nothing here.
function closedByHost(frames) {
  const closedByPage = new Set(frames.requests('CloseUiSession').map(request => request.payload?.[0]));
  return frames.received
    .filter(message => message.type === 'UiSessionClosedEvent')
    .map(message => message.payload?.sessionId)
    .filter(sessionId => !closedByPage.has(sessionId));
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
  // Open bug: a batch delete never closes the deleted widgets' UI sessions (MD-19, MD-56).
  // https://github.com/Macro-Deck-App/Macro-Deck/issues/1548
  test.fail();
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
