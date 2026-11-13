import { randomBytes } from 'node:crypto';
import { expect, test } from '@playwright/test';
import { sessionCode } from '../loopback.js';
import { PASSWORD, USERNAME } from './seed.js';

// A Production host opens the live Platform's announcement at any moment. Dismiss it with a bare click
// event: a real click's mousedown would close whatever menu the interrupted step had just opened.
async function dismissAnnouncements(page) {
  await page.addLocatorHandler(page.locator('app-announcement-modal .modal-overlay'), async overlay => {
    await overlay.getByRole('button', { name: 'Got it' }).dispatchEvent('click');
  });
}

// Enters the desktop UI the way the bootstrapper's window does: with a session code minted from the
// host's loopback secret.
export async function openDesktop(page, host) {
  await dismissAnnouncements(page);
  const code = sessionCode(host.secret, Math.floor(Date.now() / 1000), randomBytes(16).toString('hex'));
  await page.goto(`${host.loopbackUrl}/api/auth/loopback-session?code=${code}`);
}

export function desktopProfileTrigger(page) {
  return page.locator('shared-dropdown-menu.profile-dropdown .dropdown-trigger');
}

// The deck page first paints a placeholder profile and only then the profiles it loaded.
export async function selectDesktopProfile(page, profileName) {
  const trigger = desktopProfileTrigger(page);
  await expect(trigger).toBeVisible();
  await expect(trigger).not.toHaveText(/^\s*No Profile\s*$/);
  if ((await trigger.innerText()).trim() === profileName) {
    return;
  }
  await trigger.click();
  await page.locator('.profile-list-select').filter({ hasText: profileName }).click();
  await expect(trigger).toContainText(profileName);
}

export async function openProfileMenu(page, profileName) {
  await page.getByRole('button', { name: profileName, exact: true }).click();
}

export async function clickProfileAction(page, profileName, action) {
  await openProfileMenu(page, profileName);
  const profileRow = page.locator('.profile-list-item').filter({
    has: page.getByText(profileName, { exact: true }),
  });
  await expect(profileRow).toBeVisible();
  await profileRow.getByRole('button', { name: `More actions for ${profileName}` }).click();
  await page.locator('.menu-item').filter({ hasText: new RegExp(`^\\s*${action}\\s*$`) }).click();
}

export async function openDesktopProfile(page, host, profileName) {
  await openDesktop(page, host);
  await selectDesktopProfile(page, profileName);
}

export function webDeck(page) {
  return page.locator('.wc-deck');
}

export function webTile(page, widgetId) {
  return page.locator(`.wc-deck [data-widget-id="${widgetId}"]`);
}

export function desktopTile(page, widgetId) {
  return page.locator(`shared-widget-item[data-widget-id="${widgetId}"]`);
}

export async function signInWebClient(page, { username = USERNAME, password = PASSWORD } = {}) {
  await expect(page.getByText('Sign in to continue')).toBeVisible();
  await page.getByRole('button', { name: 'Password', exact: true }).click();
  await page.getByRole('textbox', { name: 'Username' }).fill(username);
  await page.locator('input[name="password"]').fill(password);
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(webDeck(page)).toBeVisible();
}

export async function openWebClient(page, host) {
  await page.goto(host.publicUrl);
  await signInWebClient(page);
}

// Signs a fresh browser context into the Web Client and returns it together with the device the
// host registered for it, so a test can steer that device through the REST API.
export async function connectWebClient(browser, host, api) {
  const before = new Set((await api.devices()).map(device => device.id));
  const context = await browser.newContext();
  const page = await context.newPage();
  const frames = recordSocketFrames(page);
  await openWebClient(page, host);
  let device = null;
  await expect.poll(async () => {
    device = (await api.devices()).find(item => !before.has(item.id) && item.online) ?? null;
    return device !== null;
  }, { message: 'the Web Client registers a device' }).toBe(true);
  return { context, page, device, frames };
}

// Every JSON message the page sends over its UI sockets, so a test can count what one gesture sent.
export function recordSocketFrames(page) {
  const frames = { sent: [], received: [] };
  const parse = frame => {
    try {
      return JSON.parse(String(frame.payload));
    } catch {
      return null;
    }
  };
  page.on('websocket', socket => {
    socket.on('framesent', frame => {
      const message = parse(frame);
      if (message) {
        frames.sent.push(message);
      }
    });
    socket.on('framereceived', frame => {
      const message = parse(frame);
      if (message) {
        frames.received.push(message);
      }
    });
  });
  frames.requests = type => frames.sent.filter(message => message.kind === 'request' && message.type === type);
  frames.response = request => frames.received.find(message =>
    message.kind === 'response' && message.correlationId === request.id);
  frames.sessionsFor = widgetId => frames.requests('OpenWidgetUiSession')
    .filter(request => request.payload?.[0]?.widgetId === widgetId)
    .map(request => frames.response(request)?.payload)
    .filter(answer => answer?.accepted && answer.sessionId)
    .map(answer => answer.sessionId);
  // A session is live for presses once its tree has arrived: the page renders and routes from it.
  frames.hasTree = sessionId => frames.received.some(message =>
    message.type === 'UiSessionTreeUpdatedEvent' && message.payload?.sessionId === sessionId);
  frames.pressesFor = widgetId => {
    const sessions = new Set(frames.sessionsFor(widgetId));
    return frames.requests('SendUiEvent')
      .filter(request => sessions.has(request.payload?.[0]?.sessionId) && request.payload?.[0]?.name === 'press');
  };
  frames.lockRefusalsFor = widgetId => frames.received.filter(message =>
    message.type === 'ActionExecutionStatusEvent'
    && message.payload?.widgetId === widgetId
    && message.payload?.error?.code === 'HOST_LOCKED');
  return frames;
}

// A tile's press is an event on the widget's UI session, and a press before that session is open
// waits for it. Waits for a live session, presses once and proves exactly one press left the page.
export async function pressWebTile(client, widgetId, tileOf = webTile) {
  const tile = tileOf(client.page, widgetId);
  await expect(tile).toBeVisible();
  await expect.poll(() => {
    const sessions = client.frames.sessionsFor(widgetId);
    return sessions.length > 0 && client.frames.hasTree(sessions[sessions.length - 1]);
  }, { message: `widget ${widgetId} has a live UI session` }).toBe(true);
  const before = client.frames.pressesFor(widgetId).length;
  await tile.click();
  await expect.poll(() => client.frames.pressesFor(widgetId).length, { message: 'the press reaches the socket' })
    .toBe(before + 1);
}

export async function pressDesktopTile(page, frames, widgetId) {
  await pressWebTile({ page, frames }, widgetId, desktopTile);
}

// A locked host computer refuses every client press, and CI runners without an attended console report
// locked, so the lock state read before the press decides which outcome is asserted.
export async function pressWebTileAndExpect(client, api, widgetId, variable, { before, after }) {
  const { locked } = await api.get('/api/system/lock-state');
  const refusalsBefore = client.frames.lockRefusalsFor(widgetId).length;
  await pressWebTile(client, widgetId);
  if (!locked) {
    await expect.poll(() => api.variableValue(variable)).toBe(after);
    return;
  }
  test.info().annotations.push({
    type: 'host-locked',
    description: `The host reported its computer locked, so the press on ${widgetId} was checked for refusal.`,
  });
  await expect.poll(() => client.frames.lockRefusalsFor(widgetId).length, {
    message: 'the locked host refuses the press with HOST_LOCKED',
  }).toBeGreaterThan(refusalsBefore);
  expect(await api.variableValue(variable)).toBe(before);
}

// Counts full page loads, so a test can prove a change arrived over the live socket and not
// through a reload.
export function countLoads(page) {
  const counter = { loads: 0 };
  page.on('load', () => {
    counter.loads += 1;
  });
  return counter;
}

// The live-socket proof: change a button label over REST and wait for the page to paint it. A
// page that only looks connected cannot pass this, because nothing but a push brings the new text.
export async function expectLivePush(page, api, widgetId, tile = webTile, timeout = 10_000) {
  const marker = `push-${randomBytes(4).toString('hex')}`;
  await api.updateWidgetData(widgetId, { label: marker });
  await expect(tile(page, widgetId)).toContainText(marker, { timeout });
  return marker;
}

export function watchForUnexpectedFailures(page) {
  const failures = [];
  let hostOutageExpected = false;

  page.on('pageerror', error => {
    if (!hostOutageExpected) {
      failures.push(`pageerror: ${error.message}`);
    }
  });
  page.on('response', response => {
    if (!hostOutageExpected && response.status() >= 500 && response.url().includes('/api/')) {
      failures.push(`${response.status()} ${response.request().method()} ${response.url()}`);
    }
  });

  const assertNone = () => expect(failures, failures.join('\n')).toEqual([]);

  // Runs a step that deliberately takes the host down. In-flight calls are lost there however they
  // fail, so those are an expected outcome of the restart, not a regression.
  assertNone.whileHostIsDown = async run => {
    hostOutageExpected = true;
    try {
      return await run();
    } finally {
      hostOutageExpected = false;
    }
  };

  return assertNone;
}

export function desktopFolderRow(page, name) {
  return page.locator('.folder-item').filter({
    has: page.locator('.folder-name').getByText(name, { exact: true }),
  });
}

async function nameFolderInDialog(page, heading, name, confirm) {
  const dialog = page.getByRole('dialog', { name: heading });
  await expect(dialog).toBeVisible();
  await dialog.locator('input').fill(name);
  await dialog.getByRole('button', { name: confirm, exact: true }).click();
  await expect(dialog).toBeHidden();
}

export async function clickFolderAction(page, folderName, action) {
  await desktopFolderRow(page, folderName).click({ button: 'right' });
  await page.locator('.menu-item').filter({ hasText: new RegExp(`^\\s*${action}\\s*$`) }).click();
}

export async function createDesktopFolder(page, name, parentName) {
  if (parentName) {
    await clickFolderAction(page, parentName, 'New Folder Inside');
  } else {
    await page.getByRole('button', { name: 'New Folder', exact: true }).click();
  }
  await nameFolderInDialog(page, 'New Folder', name, 'Create');
  await expect(desktopFolderRow(page, name)).toBeVisible();
}

export async function renameDesktopFolder(page, name, newName) {
  await clickFolderAction(page, name, 'Rename');
  await nameFolderInDialog(page, 'Rename Folder', newName, 'Rename');
  await expect(desktopFolderRow(page, newName)).toBeVisible();
}

export async function deleteDesktopFolder(page, name) {
  await clickFolderAction(page, name, 'Delete');
  await page.getByRole('dialog', { name: 'Delete Folder' }).getByRole('button', { name: 'Delete', exact: true }).click();
  await expect(desktopFolderRow(page, name)).toHaveCount(0);
}

export async function deleteDesktopProfile(page, profileName) {
  await clickProfileAction(page, profileName, 'Delete');
  await page.getByRole('dialog', { name: 'Delete Profile' }).getByRole('button', { name: 'Delete', exact: true }).click();
  await expect(page.getByRole('dialog', { name: 'Delete Profile' })).toBeHidden();
}

export async function openDesktopBackups(page) {
  await page.getByRole('button', { name: 'Settings', exact: true }).click();
  await page.locator('button.settings-nav__item').filter({ hasText: /^\s*Backups\s*$/ }).click();
  await expect(page.getByRole('button', { name: 'Create backup' })).toBeVisible();
}

// The toast arrives before the list reloads, so the helper waits for the new row and returns its id. The
// notification inbox repeats the toast text, so only the toast counts.
export async function createDesktopBackup(page, api) {
  const before = new Set((await api.get('/api/backups')).backups.map(backup => backup.id));
  await page.getByRole('button', { name: 'Create backup' }).click();
  await expect(page.locator('.th-message').filter({ hasText: /^\s*Backup completed\s*$/ }))
    .toBeVisible({ timeout: 60_000 });
  let created = [];
  await expect.poll(async () => {
    created = (await api.get('/api/backups')).backups.filter(backup => !before.has(backup.id));
    return created.length;
  }).toBe(1);
  await expect(page.locator('li.bl-row')).toHaveCount(before.size + 1);
  return created[0].id;
}

// The dialog adds every group a chosen one depends on, so the prepare request is returned for checking.
export async function restoreDesktopBackup(page, host, backupId, components = 'all') {
  await page.locator('li.bl-row').first().locator('.bl-dots-btn').click();
  await page.locator('button.bl-menu-item').filter({ hasText: /^\s*Restore…\s*$/ }).click();
  const choose = page.getByRole('dialog', { name: 'Choose what to restore' });
  await expect(choose).toBeVisible();
  if (components !== 'all') {
    await choose.getByRole('button', { name: 'Clear all', exact: true }).click();
    for (const group of components) {
      await choose.locator(`li.brm-item[data-group="${group}"] shared-checkbox`).click();
    }
  }
  await choose.getByRole('button', { name: 'Continue', exact: true }).click();
  const prepare = page.waitForRequest(request => request.url().endsWith('/api/backups/restore/prepare'));
  await page.getByRole('dialog', { name: 'Restore this backup?' }).getByRole('button', { name: 'Restore', exact: true }).click();
  const prepared = (await prepare).postDataJSON();
  expect(prepared.backupId, 'the newest row is the backup to restore').toBe(backupId);
  const restart = page.locator('.bl-pending-restart');
  await expect(restart).toBeVisible({ timeout: 30_000 });
  const starts = host.starts;
  await restart.click();
  await host.waitForRestart(starts);
  return prepared;
}
