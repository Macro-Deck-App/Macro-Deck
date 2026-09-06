/**
 * A DOM for the handful of specs that exercise browser APIs.
 *
 * The runtime is framework-free but not environment-free: the transport talks to `WebSocket`, and
 * the input and page-zoom helpers bind real events. Those specs ran in a browser under karma before
 * this package left the Angular workspace; jsdom is what stands in for it now.
 */
const { JSDOM } = require('jsdom');

// A concrete origin, not the default `about:blank`: localStorage throws on an opaque origin, and
// the storage-backed preferences are worth testing rather than working around.
const dom = new JSDOM('<!doctype html><html><body></body></html>', {
  pretendToBeVisual: true,
  url: 'http://localhost/',
});

// Assigned unconditionally, not only where Node has no global of its own. Node ships its own
// `Event`/`EventTarget`, and mixing the two throws on dispatch: a jsdom WheelEvent is not an
// instance of Node's Event, so the window has to be the only DOM in the room.
const exposed = [
  'document', 'navigator', 'location', 'history', 'screen', 'WebSocket', 'MessageEvent', 'CloseEvent',
  'Event', 'EventTarget', 'CustomEvent', 'KeyboardEvent', 'MouseEvent', 'WheelEvent', 'PointerEvent',
  'Node', 'Element', 'HTMLElement', 'HTMLImageElement', 'Image', 'SVGElement', 'DOMException', 'getComputedStyle',
  'requestAnimationFrame', 'cancelAnimationFrame', 'matchMedia', 'MutationObserver', 'CSSStyleDeclaration',
  'localStorage', 'sessionStorage',
];

for (const name of exposed) {
  if (dom.window[name] !== undefined) globalThis[name] = dom.window[name];
}

// `window` is the global object, not a separate one. Specs swap browser constructors by assigning to
// `window.X`, which under karma patched the same object the code under test reads; keeping them
// distinct here would let those swaps silently miss.
globalThis.window = globalThis;

// Bound to jsdom's window rather than copied like the constructors above: these are methods, not
// constructors, and calling them unbound loses the `this` they need to reach jsdom's own listener
// registry. `window`-level events (`online`/`offline` among them) have nowhere else to dispatch to.
globalThis.addEventListener = dom.window.addEventListener.bind(dom.window);
globalThis.removeEventListener = dom.window.removeEventListener.bind(dom.window);
globalThis.dispatchEvent = dom.window.dispatchEvent.bind(dom.window);
