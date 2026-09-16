import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { JSDOM, VirtualConsole } from 'jsdom';

const script = readFileSync(new URL('./external-links.js', import.meta.url), 'utf8');
const APP_ORIGIN = 'http://127.0.0.1:5291';

function load(beforeScript) {
  const dom = new JSDOM('<!doctype html><body></body>', {
    url: `${APP_ORIGIN}/`,
    runScripts: 'outside-only',
    virtualConsole: new VirtualConsole(),
  });
  const { window } = dom;
  const calls = [];
  window.__TAURI_INTERNALS__ = {
    invoke: (command, args) => {
      calls.push([command, { ...args }]);
      return Promise.resolve(true);
    },
  };
  beforeScript?.(window);
  window.eval(script);
  return { window, calls };
}

function link(window, href, target = '_blank') {
  const anchor = window.document.createElement('a');
  anchor.setAttribute('href', href);
  if (target) anchor.setAttribute('target', target);
  anchor.textContent = 'link';
  window.document.body.append(anchor);
  return anchor;
}

function press(window, element, type = 'click', button = 0) {
  const event = new window.MouseEvent(type, { bubbles: true, cancelable: true, button });
  element.dispatchEvent(event);
  return event;
}

test('a left click on an external target=_blank link opens it in the OS browser', () => {
  const { window, calls } = load();
  const event = press(window, link(window, 'https://github.com/manuelmayer-dev/Macrogotchi'));

  assert.deepEqual(calls, [['open_external', { url: 'https://github.com/manuelmayer-dev/Macrogotchi' }]]);
  assert.equal(event.defaultPrevented, true);
});

test('a middle click on an external link opens it in the OS browser', () => {
  const { window, calls } = load();
  const event = press(window, link(window, 'https://example.com/docs'), 'auxclick', 1);

  assert.deepEqual(calls, [['open_external', { url: 'https://example.com/docs' }]]);
  assert.equal(event.defaultPrevented, true);
});

test('a click another handler already cancelled is left alone, so nothing opens twice', () => {
  const { window, calls } = load(w => w.addEventListener('click', event => event.preventDefault()));
  press(window, link(window, 'https://example.com/docs'));

  assert.deepEqual(calls, []);
});

test('same-origin and non-web links stay in the app', () => {
  const { window, calls } = load();
  const sameOrigin = press(window, link(window, `${APP_ORIGIN}/#/store`, null));
  const mail = press(window, link(window, 'mailto:someone@example.com'));

  assert.deepEqual(calls, []);
  assert.equal(sameOrigin.defaultPrevented, false);
  assert.equal(mail.defaultPrevented, false);
});

test('window.open of an external URL goes to the OS browser instead of a dropped new window', () => {
  const { window, calls } = load();

  assert.equal(window.open('https://example.com/', '_blank'), null);
  assert.deepEqual(calls, [['open_external', { url: 'https://example.com/' }]]);
});
