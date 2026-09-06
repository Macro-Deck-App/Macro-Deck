/*
 * The client's app-shell service worker.
 *
 * `build.mjs` replaces the MANIFEST line below with the real build: every shell file and the hash
 * that identifies the build as a whole. The worker keeps one cache per version, so an older build's
 * cache is dropped only once the newer worker actually activates - a client still running the old
 * build keeps serving from it until then.
 *
 * The worker never caches itself, and the host must never cache it either: a worker served from a
 * cache can never be replaced (see StaticAssetCachePolicy's control-file list).
 */
var MANIFEST = { version: 'dev', assets: [] };

var MESSAGE_SKIP_WAITING = 'macro-deck.skip-waiting';
var MESSAGE_VERSION = 'macro-deck.version';
var MESSAGE_UNRECOVERABLE = 'macro-deck.unrecoverable';

var CACHE_PREFIX = 'macro-deck-shell-';
var CACHE_NAME = CACHE_PREFIX + MANIFEST.version;
var SHELL_URL = new URL('index.html', self.registration.scope).href;

function precached() {
  var urls = {};
  for (var index = 0; index < MANIFEST.assets.length; index++) {
    urls[new URL(MANIFEST.assets[index], self.registration.scope).href] = true;
  }
  return urls;
}

var PRECACHED = precached();

self.addEventListener('install', function (event) {
  event.waitUntil(caches.open(CACHE_NAME).then(function (cache) {
    // `reload` skips the browser's own HTTP cache: a shell precached from a stale entry would pin
    // the client to a build the server no longer serves.
    var requests = MANIFEST.assets.map(function (asset) {
      return new Request(asset, { cache: 'reload' });
    });
    return cache.addAll(requests);
  }));
});

self.addEventListener('activate', function (event) {
  event.waitUntil(caches.keys().then(function (names) {
    return Promise.all(names.map(function (name) {
      if (name.indexOf(CACHE_PREFIX) === 0 && name !== CACHE_NAME) return caches.delete(name);
      return Promise.resolve(false);
    }));
  }).then(function () {
    return self.clients.claim();
  }));
});

self.addEventListener('message', function (event) {
  var data = event.data;
  if (!data) return;
  if (data.type === MESSAGE_SKIP_WAITING) {
    self.skipWaiting();
    return;
  }
  if (data.type === MESSAGE_VERSION && event.ports && event.ports[0]) {
    event.ports[0].postMessage({ version: MANIFEST.version });
  }
});

function reportUnrecoverable() {
  return self.clients.matchAll({ type: 'window' }).then(function (clients) {
    for (var index = 0; index < clients.length; index++) {
      clients[index].postMessage({ type: MESSAGE_UNRECOVERABLE });
    }
  });
}

function fromCache(request) {
  return caches.open(CACHE_NAME).then(function (cache) {
    return cache.match(request).then(function (response) {
      if (response) return response;
      // A precached file missing from its own cache means the cache no longer holds a usable shell.
      return fetch(request).catch(function (error) {
        return reportUnrecoverable().then(function () { throw error; });
      });
    });
  });
}

/** Whole first segments, so `/administration` is not mistaken for the configuration UI. */
var FOREIGN_SEGMENTS = ['admin', 'api', 'targets'];

function isForeignRoute(pathname) {
  var segment = pathname.split('/')[1];
  for (var index = 0; index < FOREIGN_SEGMENTS.length; index += 1) {
    if (segment === FOREIGN_SEGMENTS[index]) return true;
  }
  return false;
}

self.addEventListener('fetch', function (event) {
  var request = event.request;
  if (request.method !== 'GET') return;

  var url = new URL(request.url);
  if (url.origin !== self.location.origin) return;

  // Routes this client does not own, which its shell must never be handed for (ADR 0041, ADR 0082).
  // The worker registers at the origin root, so without this it answers the configuration UI, a
  // device target and the down-levelled entry with the deck's own shell - each of which is a
  // different application, and /legacy/ is a different bundle of this one for a browser that cannot
  // parse the modern build.
  if (isForeignRoute(url.pathname)) return;

  // A navigation is answered with the shell itself: the client is a single page, and every route it
  // owns is resolved in the browser.
  if (request.mode === 'navigate') {
    event.respondWith(fromCache(new Request(SHELL_URL)).catch(function () { return fetch(request); }));
    return;
  }

  if (PRECACHED[url.href] === true) {
    event.respondWith(fromCache(request));
  }
});
