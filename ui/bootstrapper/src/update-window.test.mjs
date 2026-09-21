import { afterEach, test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { JSDOM, VirtualConsole } from 'jsdom';

const html = readFileSync(new URL('./update-window.html', import.meta.url), 'utf8');
const script = readFileSync(new URL('./update-window.js', import.meta.url), 'utf8');
const pageCss = readFileSync(new URL('./update-window.css', import.meta.url), 'utf8');
const appTokens = readFileSync(new URL('../../runtime/styles/tokens.css', import.meta.url), 'utf8');

const NOTES = [
  "## What's Changed",
  '### Fixes',
  '* Reconnect plugins by @suchbyte in https://github.com/Macro-Deck-App/Macro-Deck/pull/951',
  '* Fix a crash in https://github.com/other/repo/issues/12',
  '',
  '**Full Changelog**: https://github.com/Macro-Deck-App/Macro-Deck/compare/v3.0.0...v3.1.0',
].join('\n');

const windows = [];

afterEach(() => {
  while (windows.length > 0) {
    windows.pop().close();
  }
});

function baseView(overrides = {}) {
  return {
    lang: 'de',
    title: 'Update available',
    heading: 'Macro Deck 3.1.0',
    currentVersion: 'You currently have 3.0.0',
    changelogHeading: "What's new",
    notes: NOTES,
    noChangelog: 'No release notes were published for this version.',
    status: null,
    downloading: false,
    progressPercent: null,
    actions: [
      { id: 'later', label: 'Later', primary: false },
      { id: 'install', label: 'Download & install', primary: true },
    ],
    ...overrides,
  };
}

async function load(view) {
  const dom = new JSDOM(html, {
    url: 'macrodeck-update://localhost/',
    runScripts: 'outside-only',
    virtualConsole: new VirtualConsole(),
  });
  const { window } = dom;
  windows.push(window);
  const posts = [];
  const state = { view };
  window.fetch = async (url, options = {}) => {
    if (options.method === 'POST') {
      posts.push(url);
      return { ok: true, status: 204 };
    }
    return { ok: true, status: 200, json: async () => state.view };
  };
  window.eval(script);
  await settle(window);
  return { window, document: window.document, posts, state };
}

function settle(window) {
  return new Promise(resolve => window.setTimeout(resolve, 20));
}

test('shows the version, the changelog and the offered actions', async () => {
  const { document } = await load(baseView());

  assert.equal(document.title, 'Update available');
  assert.equal(document.getElementById('heading').textContent, 'Macro Deck 3.1.0');
  const changelog = document.getElementById('changelog');
  assert.equal(changelog.querySelector('h3').textContent, 'Fixes');
  assert.ok(!changelog.textContent.includes("What's Changed"), 'the redundant top heading is dropped');
  assert.equal(changelog.querySelectorAll('li').length, 2);
  const buttons = [...document.querySelectorAll('#actions button')].map(button => button.textContent);
  assert.deepEqual(buttons, ['Later', 'Download & install']);
});

test('shortens GitHub references and links mentions', async () => {
  const { document } = await load(baseView());
  const links = [...document.querySelectorAll('#changelog a')].map(a => [a.textContent, a.href]);

  assert.deepEqual(links, [
    ['@suchbyte', 'https://github.com/suchbyte'],
    ['#951', 'https://github.com/Macro-Deck-App/Macro-Deck/pull/951'],
    ['other/repo#12', 'https://github.com/other/repo/issues/12'],
    ['v3.0.0...v3.1.0', 'https://github.com/Macro-Deck-App/Macro-Deck/compare/v3.0.0...v3.1.0'],
  ]);
});

test('release notes are shown as text, never as markup', async () => {
  const notes = '* <img src=x onerror="window.pwned = 1"> [click](javascript:alert(1))';
  const { window, document } = await load(baseView({ notes }));

  assert.equal(document.querySelector('#changelog img'), null);
  assert.equal(document.querySelector('#changelog a'), null, 'only https links become links');
  assert.ok(document.getElementById('changelog').textContent.includes('<img src=x'));
  assert.equal(window.pwned, undefined);
});

test('a version without release notes says so', async () => {
  const { document } = await load(baseView({ notes: null }));

  assert.equal(
    document.getElementById('changelog').textContent,
    'No release notes were published for this version.',
  );
});

test('a button runs its action', async () => {
  const { window, document, posts } = await load(baseView());

  document.querySelector('[data-action="install"]').click();
  await settle(window);

  assert.deepEqual(posts, ['actions/install']);
});

test('a running download shows its progress and an error is marked as one', async () => {
  const { window, document, state } = await load(baseView({
    downloading: true,
    progressPercent: 42,
    status: { kind: 'info', text: 'Downloading… 42%' },
    actions: [{ id: 'cancel', label: 'Cancel download', primary: false }],
  }));

  assert.equal(document.getElementById('progress').hidden, false);
  assert.equal(document.getElementById('bar').value, 42);
  assert.equal(document.getElementById('status').textContent, 'Downloading… 42%');

  state.view = baseView({ status: { kind: 'error', text: 'Could not download the update' } });
  await new Promise(resolve => window.setTimeout(resolve, 1200));

  const status = document.getElementById('status');
  assert.equal(document.getElementById('progress').hidden, true);
  assert.ok(status.classList.contains('update__status--error'));
});

test('no action has focus, so a stray keystroke cannot install', async () => {
  const { document } = await load(baseView());

  assert.equal(document.activeElement?.tagName, 'BODY');
});

test('the page is marked with the language of its texts', async () => {
  const { document } = await load(baseView());

  assert.equal(document.documentElement.lang, 'de');
});

test('download progress is not announced on every percent', async () => {
  const { document } = await load(baseView({
    downloading: true,
    progressPercent: 10,
    status: { kind: 'info', text: 'Downloading… 10%' },
    actions: [{ id: 'cancel', label: 'Cancel download', primary: false }],
  }));

  assert.equal(document.getElementById('status').getAttribute('aria-live'), 'off');
  assert.equal(document.getElementById('bar').getAttribute('aria-valuetext'), 'Downloading… 10%');
});

test('the page takes the theme and accent chosen in Macro Deck', async () => {
  const { document } = await load(baseView({ theme: 'light', accent: '#ff5722' }));
  const root = document.documentElement;

  assert.ok(root.classList.contains('light'));
  assert.equal(root.style.getPropertyValue('--color-accent'), '#ff5722');
});

test('an accent that is not a plain colour is ignored', async () => {
  const { document } = await load(baseView({ accent: 'red;background:url(x)' }));

  assert.equal(document.documentElement.style.getPropertyValue('--color-accent'), '');
});

function colours(css, selector) {
  const start = css.indexOf(`${selector} {`);
  const block = css.slice(start, css.indexOf('}', start));
  return Object.fromEntries([...block.matchAll(/(--color-[\w-]+):\s*(#[0-9a-f]{3,8})/gi)]
    .map(([, name, value]) => [name, value.toLowerCase()]));
}

test('the window uses the same colours as the Macro Deck app in both themes', () => {
  for (const [page, app] of [[':root', ':root'], [':root.light', '.light'], [':root:not(.dark)', '.light']]) {
    const own = colours(pageCss, page);
    const tokens = colours(appTokens, app);
    assert.ok(Object.keys(own).length > 0);
    for (const [name, value] of Object.entries(own)) {
      if (name in tokens) {
        assert.equal(value, tokens[name], `${page} ${name}`);
      }
    }
  }
});
