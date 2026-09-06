// The legacy entry's first script: everything the down-levelled bundle assumes the platform has.
// Ported from the Angular client's legacy polyfills - the list is dictated by the floor and by what
// the runtime calls, not by either client's framework.
import 'core-js/stable';
import 'regenerator-runtime/runtime';

import 'whatwg-fetch';
// The runtime's HTTP client cancels in-flight requests with an AbortSignal, and whatwg-fetch does
// not ship AbortController. The .mjs entry keeps the graph pure ESM for the ES5 bundle.
import 'abort-controller/polyfill.mjs';
import structuredCloneShim from '@ungap/structured-clone';
import ResizeObserverPolyfill from 'resize-observer-polyfill';
import 'raf/polyfill';
import { installPointerEvents } from './pointer-events.legacy.js';
import { installObjectFit } from './object-fit.legacy.js';

if (typeof window.structuredClone !== 'function') {
  window.structuredClone = function (value, options) {
    return structuredCloneShim(value, options);
  };
}

// The renderer reflows text and the modal measures itself with a ResizeObserver. Both guard on
// `typeof ResizeObserver === 'undefined'` and simply skip the reflow, so this is what makes the
// measurement happen at all on the floor engines rather than what keeps them from throwing.
if (typeof window.ResizeObserver !== 'function') {
  window.ResizeObserver = ResizeObserverPolyfill;
}

// The renderer binds every press to pointerdown/up/cancel/leave, so on an engine without Pointer
// Events (iOS 9, Android 4) a tap does nothing at all. Synthesising them from touch/mouse here
// keeps the renderer on a single input model.
if (typeof window.PointerEvent !== 'function') {
  installPointerEvents(document);
}

// Artwork and the logo are framed by object-fit, which iOS 9 ignores - the image then stretches
// instead of being fitted. The CSS down-level marks the affected rules; this reads the marker back.
installObjectFit(document);

(function () {
  var probe = document.createElement('div');
  probe.style.cssText = 'display:flex;flex-direction:column;gap:1px;position:absolute;visibility:hidden';
  probe.appendChild(document.createElement('div'));
  probe.appendChild(document.createElement('div'));
  var parent = document.body || document.documentElement;
  parent.appendChild(probe);
  var supported = probe.scrollHeight === 1;
  parent.removeChild(probe);
  if (!supported) {
    document.documentElement.className += ' no-flex-gap';
  }
})();

// crypto.getRandomValues shim for the Android <4.4 stock browser, which lacks Web Crypto. The
// runtime's `randomToken` (client id, WebSocket correlation ids) needs it; this fallback is
// non-cryptographic and deliberately scoped to those non-security-sensitive uses.
(function () {
  var globalObject = typeof window !== 'undefined' ? window : this;
  if (!globalObject.crypto) {
    globalObject.crypto = {};
  }
  if (typeof globalObject.crypto.getRandomValues !== 'function') {
    globalObject.crypto.getRandomValues = function (buffer) {
      for (var i = 0; i < buffer.length; i++) {
        buffer[i] = Math.floor(Math.random() * 256);
      }
      return buffer;
    };
  }
})();
