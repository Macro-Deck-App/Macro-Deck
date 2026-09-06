/**
 * A press, on a browser that has no Pointer Events (issue #829).
 *
 * `pointer-events.test.mjs` covers the shim's own output: given a touch, it dispatches the pointer
 * events the spec describes. That cannot fail if the renderer stops listening for them, which is
 * the failure that actually costs a user their deck. This drives the real renderer instead, over
 * the real shim, and asks the only question the issue asks: does pressing a widget do anything.
 *
 * Requires the runtime to be built (`npm run build -w @macro-deck/runtime`).
 */
import assert from 'node:assert/strict';
import test from 'node:test';
import { createRequire } from 'node:module';
import { JSDOM } from 'jsdom';

import { installPointerEvents } from './pointer-events.legacy.js';

/** A touch event as the engines below the floor deliver it: no constructor, no PointerEvent. */
function touchEvent(document, type, touches) {
  const event = document.createEvent('Event');
  event.initEvent(type, true, true);
  Object.defineProperty(event, 'changedTouches', { value: touches });
  return event;
}

const host = (emit) => ({
  localization: { translate: (scope, key) => `${scope}:${key}` },
  resourceUrl: () => null,
  now: () => Date.parse('2026-01-02T03:04:05Z'),
  culture: () => 'en-GB',
  simpleRendering: () => false,
  fontFamily: () => null,
  fontReady: () => true,
  emit: (node, event) => emit(event),
});

/**
 * A document with no Pointer Events at all, the shim installed over it, and a widget the runtime
 * rendered into it. The globals are set before the runtime is imported because the renderer reaches
 * for `document` the way a browser bundle does.
 */
function deck({ withShim = true } = {}) {
  const dom = new JSDOM('<!doctype html><body></body>', { pretendToBeVisual: true });
  delete dom.window.PointerEvent;
  globalThis.window = dom.window;
  globalThis.document = dom.window.document;
  Object.defineProperty(globalThis, 'navigator', { value: dom.window.navigator, configurable: true });
  globalThis.Element = dom.window.Element;
  globalThis.getComputedStyle = dom.window.getComputedStyle.bind(dom.window);

  if (withShim) installPointerEvents(dom.window.document);

  // The package's CommonJS build: its ESM one is emitted with extensionless specifiers, which a
  // bundler resolves and Node's own ESM loader does not.
  const { renderUiNode } = createRequire(import.meta.url)('@macro-deck/runtime');
  const container = dom.window.document.createElement('div');
  dom.window.document.body.appendChild(container);

  const emitted = [];
  const tree = { id: 'n1', type: 'ui.button', properties: { events: ['press', 'press-start'] } };
  renderUiNode(container, tree, { width: 120, height: 120 }, null, 120, host(event => emitted.push(event)));

  const button = container.querySelector('.widget-button');
  assert.ok(button, 'the runtime rendered no button to press');
  return { document: dom.window.document, button, emitted };
}

function tap(document, target, { identifier = 0, clientX = 10, clientY = 20 } = {}) {
  const touch = { identifier, target, clientX, clientY, screenX: clientX, screenY: clientY };
  document.dispatchEvent(touchEvent(document, 'touchstart', [touch]));
  document.dispatchEvent(touchEvent(document, 'touchend', [touch]));
}

test('a touch presses a widget on a browser with no Pointer Events', () => {
  const { document, button, emitted } = deck();

  tap(document, button);

  assert.deepEqual(emitted, ['press-start', 'press']);
});

test('without the shim the same touch does nothing, which is what the shim is for', () => {
  // The counterexample: if this ever emitted a press, the test above would prove nothing about the
  // shim, because the renderer would be listening for touches on its own.
  const { document, button, emitted } = deck({ withShim: false });

  tap(document, button);

  assert.deepEqual(emitted, []);
});

test('a touch that slides off the widget before lifting still presses it', () => {
  // Real Pointer Events capture the pointer on the element the press started on, so a finger that
  // drifts off a button and lifts still completes the press. The shim has to match that, or a deck
  // becomes unusable for anyone whose finger is not perfectly still.
  const { document, button, emitted } = deck();
  const touch = { identifier: 0, target: button, clientX: 10, clientY: 20, screenX: 10, screenY: 20 };

  document.dispatchEvent(touchEvent(document, 'touchstart', [touch]));
  document.dispatchEvent(touchEvent(document, 'touchmove',
    [{ ...touch, target: document.body, clientX: 400, clientY: 400 }]));
  document.dispatchEvent(touchEvent(document, 'touchend',
    [{ ...touch, target: document.body, clientX: 400, clientY: 400 }]));

  assert.deepEqual(emitted, ['press-start', 'press']);
});

test('a cancelled touch ends the press without reporting one', () => {
  const { document, button, emitted } = deck();
  const touch = { identifier: 0, target: button, clientX: 10, clientY: 20, screenX: 10, screenY: 20 };

  document.dispatchEvent(touchEvent(document, 'touchstart', [touch]));
  document.dispatchEvent(touchEvent(document, 'touchcancel', [touch]));

  assert.ok(emitted.includes('press-start'));
  assert.ok(!emitted.includes('press'), 'a cancelled gesture is not a press');
});
