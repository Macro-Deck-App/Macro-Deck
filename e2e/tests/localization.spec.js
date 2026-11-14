import { randomBytes } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { expect, test, useIsolatedHost } from './support/fixtures.js';
import { sessionCode } from './loopback.js';
import { connectWebClient, desktopProfileTrigger, webTile } from './support/pages.js';

const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const CATALOGS = [
  { namespace: 'macrodeck.app', base: 'host/src/MacroDeckHost.Localization/Localization/Strings' },
  { namespace: 'macrodeck', base: 'sdk/src/MacroDeck.Localization/Resources/Strings' },
];

const PROFILE_NAME = 'E2E Lokalisierung';
const BUTTON_LABEL = 'Knopf';
const VARIABLE_NAME = 'e2e_l10n_wert';
const SEEDED_TEXT = [PROFILE_NAME, BUTTON_LABEL, VARIABLE_NAME];

const PLACEHOLDER = /\{[A-Za-z_][A-Za-z0-9_.]*\}/;
const RAW_KEY = /\[\[[^\]]+\]\]/;

function decodeXml(text) {
  return text
    .replace(/&#x([0-9a-f]+);/gi, (_, hex) => String.fromCodePoint(parseInt(hex, 16)))
    .replace(/&#(\d+);/g, (_, decimal) => String.fromCodePoint(Number(decimal)))
    .replaceAll('&lt;', '<')
    .replaceAll('&gt;', '>')
    .replaceAll('&quot;', '"')
    .replaceAll('&apos;', '\'')
    .replaceAll('&amp;', '&');
}

function normalize(text) {
  return text.replace(/\s+/g, ' ').trim();
}

function readResx(path) {
  const values = new Map();
  const xml = readFileSync(path, 'utf8');
  for (const match of xml.matchAll(/<data name="([^"]+)"[^>]*>\s*<value>([\s\S]*?)<\/value>/g)) {
    values.set(match[1], normalize(decodeXml(match[2])));
  }
  return values;
}

function readCatalogs() {
  const english = new Map();
  const german = new Map();
  for (const { namespace, base } of CATALOGS) {
    for (const [key, value] of readResx(join(repositoryRoot, `${base}.resx`))) {
      english.set(`${namespace}:${key}`, value);
    }
    for (const [key, value] of readResx(join(repositoryRoot, `${base}.de.resx`))) {
      german.set(`${namespace}:${key}`, value);
    }
  }
  return { english, german };
}

function escapeRegExp(text) {
  return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

function matcherFor(sentence) {
  if (!PLACEHOLDER.test(sentence)) {
    return text => text.includes(sentence);
  }
  const pattern = new RegExp(sentence.split(/\{[A-Za-z_][A-Za-z0-9_.]*\}/g).map(escapeRegExp).join('.+?'));
  return text => pattern.test(text);
}

function englishSentences({ english, german }, seeded) {
  const germanCorpus = [...german.values()];
  const sentences = [];
  for (const [key, value] of english) {
    const translation = german.get(key);
    if (!translation || translation === value) {
      continue;
    }
    const literal = value.replace(new RegExp(PLACEHOLDER.source, 'g'), ' ');
    if (!/\p{L}/u.test(literal) || literal.split(/\s+/).filter(word => /\p{L}/u.test(word)).length < 3) {
      continue;
    }
    const matches = matcherFor(value);
    if (germanCorpus.some(matches) || seeded.some(matches)) {
      continue;
    }
    sentences.push({ key, value, matches });
  }
  return sentences;
}

const catalogs = readCatalogs();
const de = key => {
  const value = catalogs.german.get(`macrodeck.app:${key}`) ?? catalogs.german.get(`macrodeck:${key}`);
  expect(value, `German value of ${key}`).toBeTruthy();
  return value;
};
const en = key => catalogs.english.get(`macrodeck.app:${key}`) ?? catalogs.english.get(`macrodeck:${key}`);
const ENGLISH_SENTENCES = englishSentences(catalogs, SEEDED_TEXT);

function violations(rawText) {
  const text = normalize(rawText);
  const found = [];
  for (const pattern of [RAW_KEY, PLACEHOLDER]) {
    const match = text.match(pattern);
    if (match) {
      found.push(`${pattern}: ${match[0]}`);
    }
  }
  if (text.includes('[object Object]')) {
    found.push('[object Object]');
  }
  for (const sentence of ENGLISH_SENTENCES) {
    if (sentence.matches(text)) {
      found.push(`English ${sentence.key}: ${sentence.value}`);
    }
  }
  return found;
}

async function expectGermanPage(page, where) {
  await expect.poll(async () => violations(await page.locator('body').innerText()), {
    message: `${where} shows only German text`,
    timeout: 10_000,
  }).toEqual([]);
}

async function useGerman(api) {
  await api.put('/api/settings/localization', { culture: 'de' });
}

// pages.js dismisses the Platform announcement by its English button, which a German UI never shows.
async function openGermanDesktop(page, host) {
  const gotIt = new RegExp(`^(${escapeRegExp(de('Dialogs.Notice.GotIt'))}|${escapeRegExp(en('Dialogs.Notice.GotIt'))})$`);
  await page.addLocatorHandler(page.locator('app-announcement-modal .modal-overlay'), async overlay => {
    await overlay.getByRole('button', { name: gotIt }).dispatchEvent('click');
  });
  const code = sessionCode(host.secret, Math.floor(Date.now() / 1000), randomBytes(16).toString('hex'));
  await page.goto(`${host.loopbackUrl}/api/auth/loopback-session?code=${code}`);
  await expect(desktopProfileTrigger(page)).toBeVisible();
}

// Pages are sidebar links, panels are sidebar buttons, and a badge count joins the accessible name.
function navEntry(page, role, key) {
  return page.getByRole(role, { name: new RegExp(`(^|\\s)${escapeRegExp(de(key))}(\\s|$)`) });
}

let seeded = null;

useIsolatedHost('localization', {
  prepare: async api => {
    await api.completeOnboarding();
    const profile = await api.profile(PROFILE_NAME, { columns: 3, rows: 2 });
    const button = await api.actionButton(profile.rootFolder.id, { label: BUTTON_LABEL });
    await api.variable(VARIABLE_NAME, { type: 'numeric', value: 3 });
    seeded = { profile, button };
  },
});

test('the catalogs yield English sentences to look for', () => {
  expect(ENGLISH_SENTENCES.length).toBeGreaterThan(500);
});

test('the desktop UI shows only German on the deck, settings, automations, variables and integrations', async ({ page, api, host }) => {
  test.setTimeout(120_000);
  await useGerman(api);
  await openGermanDesktop(page, host);
  await expect.poll(() => page.evaluate(() => document.documentElement.lang)).toBe('de');
  await expectGermanPage(page, 'the deck');

  await navEntry(page, 'button', 'Settings.Title').click();
  await expect(page.getByRole('dialog')).toBeVisible();
  await expectGermanPage(page, 'the settings dialog');
  await page.keyboard.press('Escape');
  await expect(page.getByRole('dialog')).toHaveCount(0);

  for (const [key, where] of [
    ['Nav.Automations', 'the automations page'],
    ['Nav.Variables', 'the variables page'],
    ['Nav.Integrations', 'the integrations page'],
    ['Nav.Deck', 'the deck after navigating back'],
  ]) {
    await navEntry(page, 'link', key).click();
    await expectGermanPage(page, where);
  }
  await expect(page.getByText(VARIABLE_NAME)).toHaveCount(0);
});

async function webClientInGerman(browser, api, host) {
  await api.useEnglish();
  const web = await connectWebClient(browser, host, api);
  await api.openProfileOnDevice(web.device.id, seeded.profile.id);
  await expect(webTile(web.page, seeded.button.id)).toContainText(BUTTON_LABEL);
  const catalogReloaded = web.page.waitForResponse(response =>
    new URL(response.url()).pathname === '/api/localization' && response.ok());
  await useGerman(api);
  await catalogReloaded;
  return web;
}

test('the Web Client deck and its settings switch to German and show only German', async ({ browser, api, host }) => {
  const web = await webClientInGerman(browser, api, host);
  await expectGermanPage(web.page, 'the Web Client deck');

  await web.page.locator('.wc-client-settings-trigger').click();
  await expect(web.page.getByText(de('Settings.Appearance.Dark'), { exact: true })).toBeVisible();
  await expectGermanPage(web.page, 'the Web Client settings');
  await web.context.close();
});

test('the Web Client settings button is relabelled in German without a reload', async ({ browser, api, host }) => {
  const web = await webClientInGerman(browser, api, host);
  await expect(web.page.locator('.wc-client-settings-trigger'))
    .toHaveAccessibleName(de('WebClient.Settings.OpenSettings'));
  await web.context.close();
});

test('an open Web Client settings dialog switches to German without closing', async ({ browser, api, host }) => {
  await api.useEnglish();
  const web = await connectWebClient(browser, host, api);
  await web.page.locator('.wc-client-settings-trigger').click();
  const dialog = web.page.locator('.wc-client-settings-dialog');
  await expect(dialog.locator('.wc-client-settings-title')).toHaveText(en('Settings.Title'));
  await dialog.evaluate(node => node.setAttribute('data-e2e-opened', 'before'));

  const catalogReloaded = web.page.waitForResponse(response =>
    new URL(response.url()).pathname === '/api/localization' && response.ok());
  await useGerman(api);
  await catalogReloaded;

  await expect(dialog.locator('.wc-client-settings-title')).toHaveText(de('Settings.Title'));
  await expect(dialog).toHaveAttribute('data-e2e-opened', 'before');
  await expect(dialog.getByText(de('Settings.Appearance.Dark'), { exact: true })).toBeVisible();
  await expect(dialog.getByText(de('WebClient.Account.SignOut'), { exact: true })).toBeVisible();
  await expect(dialog.locator('.wc-client-settings-close')).toHaveAccessibleName(de('Common.Close'));
  await expectGermanPage(web.page, 'the Web Client settings left open across the switch');
  await web.context.close();
});

test('an open Web Client device setup wizard switches to German without closing', async ({ browser, api, host }) => {
  await api.useEnglish();
  const web = await connectWebClient(browser, host, api);
  await web.page.locator('.wc-client-settings-trigger').click();
  await web.page.getByRole('button', { name: en('WebClient.DeviceSetup.OpenAction'), exact: true }).click();
  const wizard = web.page.locator('.wc-setup-dialog');
  await expect(wizard.locator('.wc-setup-title')).toHaveText(en('WebClient.DeviceSetup.Title'));
  await wizard.evaluate(node => node.setAttribute('data-e2e-opened', 'before'));

  const catalogReloaded = web.page.waitForResponse(response =>
    new URL(response.url()).pathname === '/api/localization' && response.ok());
  await useGerman(api);
  await catalogReloaded;

  await expect(wizard.locator('.wc-setup-title')).toHaveText(de('WebClient.DeviceSetup.Title'));
  await expect(wizard).toHaveAttribute('data-e2e-opened', 'before');
  await expect(wizard.getByText(de('WebClient.DeviceSetup.StepCertificate'), { exact: true })).toBeVisible();
  await expect(wizard.locator('.wc-setup-close')).toHaveAccessibleName(de('Common.Close'));
  await expectGermanPage(web.page, 'the device setup wizard left open across the switch');
  await web.context.close();
});

test('a host notification is raised in German and shown in German in the notifications panel', async ({ page, api, host, playwright }) => {
  await useGerman(api);
  await api.call('DELETE', '/api/notifications', undefined, { expectOk: false });
  const username = `e2e-falsch-${randomBytes(3).toString('hex')}`;
  const anonymous = await playwright.request.newContext();
  const login = await anonymous.post(`${host.publicUrl}/api/auth/login`, {
    data: { username, password: 'not-the-password', scope: 'client' },
  });
  expect(login.ok()).toBe(false);
  await anonymous.dispose();

  const title = de('Notifications.FailedLogin');
  expect(title).not.toBe(en('Notifications.FailedLogin'));
  const template = de('Notifications.FailedLoginMessage.One').replace('{username}', username);
  const message = new RegExp(`^${template.split('{ip}').map(escapeRegExp).join('.+')}$`);

  let notification = null;
  await expect.poll(async () => {
    notification = (await api.get('/api/notifications')).notifications.find(item => item.message?.includes(username));
    return notification?.title ?? null;
  }, { message: 'the failed login raises a notification' }).toBe(title);
  expect(notification.message).toMatch(message);

  await openGermanDesktop(page, host);
  await navEntry(page, 'button', 'Nav.Notifications').click();
  const panel = page.getByRole('complementary', { name: de('Nav.Notifications'), exact: true });
  await expect(panel).toContainText(title);
  await expect(panel).toContainText(notification.message);
  await expectGermanPage(page, 'the notifications panel');
});
