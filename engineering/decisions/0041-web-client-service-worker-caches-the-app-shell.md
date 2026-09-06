# ADR 0041: The web client's service worker caches the app shell and nothing else

Status: Accepted

## Context

The web client is used as an always-ready control surface — a tablet on a wall, woken for a single
time-critical press. It was always a browser tab with browser chrome and a cold shell, and nothing could
keep the display awake, because both installability and `navigator.wakeLock` are secure-context features
([ADR 0040](0040-public-listeners-and-tls.md) is what makes a secure origin available).

A service worker is a persistent, origin-scoped interceptor, and on this origin that is unusually
delicate. The same origin serves the web client at `/`, the configuration UI at `/admin`, the
down-levelled ES5 client at `/legacy`, per-device target builds under `/targets/<id>`, the API, the UI
WebSocket, and every icon, artwork and profile payload the deck renders — all of it either user content
or session-bearing. A worker that caches the wrong response does not fail loudly; it serves a stale deck,
or another session's data, to a client that has no way to tell.

## Decision

**The worker caches the versioned app shell and hashed static assets, and nothing else.** The client
ships a hand-written worker in [`src/pwa/service-worker.js`](../../ui/web-client/src/pwa/service-worker.js)
whose precache manifest [`build.mjs`](../../ui/web-client/build.mjs) writes as its last step and
[`ci/scripts/verify-worker-manifest.mjs`](../../ci/scripts/verify-worker-manifest.mjs) re-derives from
disk in CI. The failure it prevents is silent — later build steps rewrite `index.html`, and a manifest
written before them describes bytes that no longer exist — so it is checked mechanically rather than
remembered.

**There are no data caches, and there never will be.** The worker only intercepts a request one of its
groups matches; everything else goes to the network untouched. The absence of a data group is therefore
the *mechanism* — not a comment — that keeps `/api/auth/*`, every other REST response, and every profile,
widget, variable and icon payload out of `CacheStorage`. Adding one would silently make user content and
session state cacheable.

**Navigations to `/admin`, `/legacy`, `/targets` and `/api` are excluded from the worker's navigation
handling.** The worker's scope is the origin root, so it does claim them; the exclusion is what lets the
host's own SPA fallbacks answer instead of the web-client shell being served in place of the
configuration UI.

**The ES5 `/legacy` entry is not a PWA.** Its HTML is stripped of the manifest link and the Apple
web-app metas and carries a marker meta the app checks before registering a worker. A browser routed
there cannot run the modern bundle, so telling it that it can install the app would produce a standalone
window with none of the capabilities the install implies. The *marker*, rather than a path check, carries
this, because the catch-all route rewrites `/legacy/` to `/` before registration runs.

**Registration is refused on an insecure origin, in development, and where the API is absent — and the
client reports which.** "This address cannot do it" and "this browser cannot do it" are different
problems with different fixes, and collapsing them sends a user chasing a browser update that cannot
help.

**A new version is activated while the document is hidden**, plus an explicit "Update now" control. A
press surface must not reload under the user's finger.

**The worker's control files are served `no-store`.** A cached worker or manifest pins a client to an old
app version across a host upgrade, and no update — automatic or manual — can dislodge it.

## Consequences

- Install and "keep display awake" are unavailable on the plain-HTTP listener, by construction. That is
  not a defect to work around: both are secure-context features, and the HTTPS listener needs a
  certificate the *device* actually trusts — a clicked-through warning leaves the origin in a state where
  browsers refuse to register a worker at all. `LocalClientEndpoint` deliberately stays plain HTTP while
  an HTTP listener exists, so a local client and the adb-tunnelled Android path legitimately see the
  "unavailable on this address" state.
- Moving a device from the HTTP origin to the HTTPS one is an origin change: `localStorage`, the refresh
  cookie and the device identity do not carry over. Nothing in the client can prevent that.
- A registered worker outlives the code that registered it, so reverting does not uninstall it. Deploying
  a no-op worker in its place is the only rollback that reaches clients already in the field.
- The stale-shell version check has to activate a pending worker update before reloading; without that
  its cache-busted reload is answered from the worker's cache and the check escalates against a perfectly
  healthy host.

## Alternatives considered

- **Caching REST responses for an offline deck.** The deck is a remote control. A cached deck that
  renders but cannot act is worse than an honest "connecting", and the payloads are user content.
- **Serving the PWA from the plain-HTTP origin.** Not possible, and pretending otherwise ships a dead
  toggle.
- **Making `/legacy` its own origin so a root worker could not reach it.** Disproportionate: a second
  listener or a path-scoped registration, where the navigation exclusion plus the marker already produce
  the required behaviour.
