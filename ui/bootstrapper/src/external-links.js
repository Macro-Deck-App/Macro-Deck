// Injected into every page of the main window: routes links that point outside
// the app to the user's default browser.
//
// The WebView handles no new-window request, so `window.open` and a
// `target="_blank"` anchor are silently dropped - a user clicking an external
// link (the Spotify Developer Dashboard, an OAuth consent page) sees nothing
// happen at all (issue #133). A cross-origin link without a target is worse: it
// would navigate the app window itself away to a remote site, with no browser
// chrome to come back from.
//
// UI code that knows about the shell calls `macroDeckShell.openExternal`
// directly (the shared ExternalLinkService) and cancels its own event; this is
// the catch-all beneath that, so a plain link anywhere in the app still works.
// The listeners run in the bubble phase and skip an already-cancelled event, so
// they never open a link twice.
(function () {
  'use strict';

  function openExternal(url) {
    var internals = window.__TAURI_INTERNALS__;
    if (!internals || typeof internals.invoke !== 'function') {
      return false;
    }
    try {
      internals.invoke('open_external', { url: url });
    } catch (error) {
      return false;
    }
    return true;
  }

  function externalUrl(raw) {
    if (raw === undefined || raw === null || raw === '') {
      return null;
    }
    var url;
    try {
      url = new URL(String(raw), window.location.href);
    } catch (error) {
      return null;
    }
    if (url.protocol !== 'http:' && url.protocol !== 'https:') {
      return null;
    }
    if (url.origin === window.location.origin) {
      return null;
    }
    return url.href;
  }

  var nativeOpen = typeof window.open === 'function' ? window.open.bind(window) : null;
  window.open = function (url, target, features) {
    var external = externalUrl(url);
    if (external && openExternal(external)) {
      return null;
    }
    return nativeOpen ? nativeOpen(url, target, features) : null;
  };

  function onLinkActivated(event) {
    if (event.defaultPrevented) {
      return;
    }
    if (event.button !== 0 && event.button !== 1) {
      return;
    }
    var target = event.target;
    if (!target || typeof target.closest !== 'function') {
      return;
    }
    var anchor = target.closest('a[href]');
    if (!anchor || anchor.hasAttribute('download')) {
      return;
    }
    var external = externalUrl(anchor.getAttribute('href'));
    if (external && openExternal(external)) {
      event.preventDefault();
    }
  }

  window.addEventListener('click', onLinkActivated);
  window.addEventListener('auxclick', onLinkActivated);
})();
