// Drives the real renderer over the real touch shim; requires the runtime to be built first
// (npm run build -w @macro-deck/runtime).
import assert from 'node:assert/strict';
import test from 'node:test';
import { createRequire } from 'node:module';
import { JSDOM } from 'jsdom';

import { installPointerEvents } from './pointer-events.legacy.js';

function touchEvent(document, type, touches) {
  const event = document.createEvent('Event');
  event.initEvent(type, true, true);
  Object.defineProperty(event, 'changedTouches', { value: touches });
  return event;
}

function pad() {
  const dom = new JSDOM('<!doctype html><body></body>', { pretendToBeVisual: true });
  delete dom.window.PointerEvent;
  globalThis.window = dom.window;
  globalThis.document = dom.window.document;
  Object.defineProperty(globalThis, 'navigator', { value: dom.window.navigator, configurable: true });
  globalThis.Element = dom.window.Element;
  globalThis.getComputedStyle = dom.window.getComputedStyle.bind(dom.window);

  installPointerEvents(dom.window.document);

  const { renderUiNode } = createRequire(import.meta.url)('@macro-deck/runtime');
  const container = dom.window.document.createElement('div');
  dom.window.document.body.appendChild(container);

  const emitted = [];
  const tree = { id: 'pad', type: 'ui.stack', properties: { events: ['pointer-down', 'pointer-up', 'tap'] } };
  renderUiNode(container, tree, { width: 120, height: 120 }, null, 120, {
    localization: { translate: (scope, key) => `${scope}:${key}` },
    resourceUrl: () => null,
    now: () => 0,
    culture: () => 'en-GB',
    simpleRendering: () => false,
    fontFamily: () => null,
    fontReady: () => true,
    emit: (node, name, payload) => emitted.push({ name, payload }),
  });

  return { document: dom.window.document, element: container.querySelector('[data-node-id="pad"]'), emitted };
}

test('a two-finger touch reaches a pad as two pointers and a two-finger tap', () => {
  const { document, element, emitted } = pad();
  const first = { identifier: 0, target: element, clientX: 10, clientY: 20, screenX: 10, screenY: 20 };
  const second = { identifier: 1, target: element, clientX: 60, clientY: 20, screenX: 60, screenY: 20 };

  const start = touchEvent(document, 'touchstart', [first]);
  document.dispatchEvent(start);
  document.dispatchEvent(touchEvent(document, 'touchstart', [second]));
  document.dispatchEvent(touchEvent(document, 'touchend', [second]));
  document.dispatchEvent(touchEvent(document, 'touchend', [first]));

  assert.deepEqual(emitted.map(entry => entry.name), ['pointer-down', 'pointer-down', 'pointer-up', 'pointer-up', 'tap']);
  const downs = emitted.filter(entry => entry.name === 'pointer-down').map(entry => entry.payload.id);
  assert.notEqual(downs[0], downs[1]);
  assert.deepEqual(emitted.at(-1).payload, { pointers: 2 });
  assert.ok(start.defaultPrevented, 'the native touch handling was not cancelled for a streamed finger');
});
