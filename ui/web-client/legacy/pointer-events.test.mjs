import assert from 'node:assert/strict';
import test from 'node:test';
import { JSDOM } from 'jsdom';

import { installPointerEvents } from './pointer-events.legacy.js';

const POINTER_TYPES = [
  'pointerdown', 'pointerup', 'pointermove', 'pointercancel',
  'pointerover', 'pointerout', 'pointerenter', 'pointerleave',
];

function fixture() {
  const dom = new JSDOM('<!doctype html><body><div id="outer"><button id="target"></button></div></body>');
  const { document } = dom.window;
  const uninstall = installPointerEvents(document);
  const seen = [];
  for (const type of POINTER_TYPES) {
    document.addEventListener(type, event => {
      seen.push({ type, target: event.target.id, pointerId: event.pointerId, pointerType: event.pointerType });
    }, true);
  }
  return { dom, document, seen, uninstall, target: document.getElementById('target') };
}

/**
 * A touch event as the engines below the floor deliver it. The shim reads `changedTouches` and the
 * modifier flags off the event and nothing else, and none of those engines have a constructor for
 * one, which is the whole reason the shim exists.
 */
function touchEvent(document, type, touches) {
  const event = document.createEvent('Event');
  event.initEvent(type, true, true);
  Object.defineProperty(event, 'changedTouches', { value: touches });
  return event;
}

function touch(target, { identifier = 0, clientX = 10, clientY = 20 } = {}) {
  return { identifier, target, clientX, clientY, screenX: clientX, screenY: clientY };
}

test('a touch synthesises the press lifecycle the renderer binds', () => {
  const { document, seen, target } = fixture();

  target.dispatchEvent(touchEvent(document, 'touchstart', [touch(target)]));
  target.dispatchEvent(touchEvent(document, 'touchend', [touch(target)]));

  const types = seen.filter(entry => entry.target === 'target').map(entry => entry.type);
  assert.ok(types.indexOf('pointerdown') !== -1);
  assert.ok(types.indexOf('pointerup') > types.indexOf('pointerdown'));
  assert.ok(types.indexOf('pointerleave') > types.indexOf('pointerup'));
});

test('a synthesised pointer event carries the coordinates and identity of its touch', () => {
  const { document, seen, target } = fixture();

  target.dispatchEvent(touchEvent(document, 'touchstart', [touch(target, { identifier: 3, clientX: 42, clientY: 7 })]));

  const down = seen.find(entry => entry.type === 'pointerdown');
  assert.equal(down.pointerType, 'touch');
  assert.equal(typeof down.pointerId, 'number');
  const event = [];
  document.addEventListener('pointermove', e => event.push(e), true);
  target.dispatchEvent(touchEvent(document, 'touchmove', [touch(target, { identifier: 3, clientX: 42, clientY: 7 })]));
  assert.equal(event[0].clientX, 42);
  assert.equal(event[0].clientY, 7);
  assert.equal(event[0].pointerId, down.pointerId);
});

test('a touch that slides off its target still ends on the element it started on', () => {
  // Implicit pointer capture: a real Pointer Events engine suppresses boundary events for a
  // captured pointer, so a finger that leaves the button and lifts still completes the press.
  const { document, seen, target } = fixture();

  target.dispatchEvent(touchEvent(document, 'touchstart', [touch(target)]));
  const outer = document.getElementById('outer');
  outer.dispatchEvent(touchEvent(document, 'touchend', [touch(outer, { clientX: 300, clientY: 300 })]));

  const up = seen.find(entry => entry.type === 'pointerup');
  assert.equal(up.target, 'target');
});

test('a cancelled touch reaches the renderer as pointercancel', () => {
  const { document, seen, target } = fixture();

  target.dispatchEvent(touchEvent(document, 'touchstart', [touch(target)]));
  target.dispatchEvent(touchEvent(document, 'touchcancel', [touch(target)]));

  assert.ok(seen.some(entry => entry.type === 'pointercancel' && entry.target === 'target'));
});

test('a handler that prevents the synthesised pointerdown prevents the touch it came from', () => {
  // What makes the engine suppress its compatibility mouse sequence and the native tap highlight.
  const { document, target } = fixture();
  document.addEventListener('pointerdown', event => event.preventDefault(), true);

  const event = touchEvent(document, 'touchstart', [touch(target)]);
  target.dispatchEvent(event);

  assert.equal(event.defaultPrevented, true);
});

test('a mouse press synthesises a primary mouse pointer', () => {
  const { document, seen, target } = fixture();

  target.dispatchEvent(new document.defaultView.MouseEvent('mousedown', { bubbles: true, clientX: 5, clientY: 6 }));
  target.dispatchEvent(new document.defaultView.MouseEvent('mouseup', { bubbles: true, clientX: 5, clientY: 6 }));

  const synthesised = seen.filter(entry => entry.pointerType === 'mouse');
  assert.deepEqual(synthesised.map(entry => entry.type), ['pointerdown', 'pointerup']);
  assert.equal(synthesised[0].pointerId, 1);
});

test('the compatibility mouse events a touch produces do not press a second time', () => {
  const { document, seen, target } = fixture();

  target.dispatchEvent(touchEvent(document, 'touchstart', [touch(target, { clientX: 10, clientY: 20 })]));
  target.dispatchEvent(touchEvent(document, 'touchend', [touch(target, { clientX: 10, clientY: 20 })]));
  target.dispatchEvent(new document.defaultView.MouseEvent('mousedown', { bubbles: true, clientX: 10, clientY: 20 }));
  target.dispatchEvent(new document.defaultView.MouseEvent('mouseup', { bubbles: true, clientX: 10, clientY: 20 }));

  assert.equal(seen.filter(entry => entry.type === 'pointerdown').length, 1);
});

test('pointer capture is a no-op the renderer can call unguarded', () => {
  const { document, target } = fixture();

  assert.equal(typeof target.setPointerCapture, 'function');
  target.setPointerCapture(1);
  assert.equal(target.hasPointerCapture(1), true);
  target.releasePointerCapture(1);
  assert.equal(target.hasPointerCapture(1), false);
});

test('uninstalling stops the synthesis', () => {
  const { document, seen, target, uninstall } = fixture();
  uninstall();

  target.dispatchEvent(touchEvent(document, 'touchstart', [touch(target)]));

  assert.deepEqual(seen, []);
});
