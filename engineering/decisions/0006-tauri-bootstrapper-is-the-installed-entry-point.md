# ADR 0006: A Tauri bootstrapper is the installed entry point

Status: Accepted

## Context

The desktop shell was an Electron main process: it spawned the host, provided the tray icon, opened a
window loading the Angular UI **from the host over HTTP**, and carried the installer and updater. For
that narrow job it shipped a full Chromium runtime (~300 MB on top of the host's ~170 MB) and
Chromium-sized installers. Because the Angular bundle was never packed into Electron — it ships in the
host's `wwwroot` — the shell was replaceable without touching UI delivery
([#57](https://github.com/Macro-Deck-App/Macro-Deck/issues/57)).

Three further problems came from where responsibilities sat.

The packaged window loaded from the fixed public origin, so the UI had to discover the random loopback
port through the shell bridge before it could make any API call, every request was cross-origin, and a
broken bridge froze the app on the splash screen.

Autostart lived entirely in the shell (`tauri-plugin-autostart` plus a JSON side file), so the setting
was unreachable from the admin UI served over the LAN, the WebView → bridge → IPC → plugin chain failed
silently, and the OS entry and the side file were two sources of truth that could diverge.

And every operating system attributes a notification to the process that posted it, so a host-posted
pairing request arrived as "Script Editor" or with no application identity at all. Only the signed,
notarized bundle carries the identity the system will show.

## Decision

**[`ui/bootstrapper/`](../../ui/bootstrapper/) is a Tauri v2 Rust application and the installed entry
point on every platform.** It spawns and supervises the bundled host, owns the tray icon,
close-to-tray, single instance, a daily-rotating shell log beside the host's, and the update flow
including `POST /api/host/shutdown?reason=update`. The UI renders in the operating system's WebView
(WebView2, WKWebView, WebKitGTK). Packaging uses the Tauri bundler (NSIS,
`.app`/DMG, AppImage, DEB, RPM) with the host staged as bundle resources, and updates use
`tauri-plugin-updater` with minisign-signed payloads.

The Electron preload bridge becomes an injected shell bridge (`src/shell-bridge.js`) exposing
`window.macroDeckShell`, implemented as Tauri commands.

### The bootstrapper owns the loopback port; the window is same-origin

It picks the loopback port before spawning the host, passes it via `MACRODECK_HOST_PORT`, and persists
it so the window origin — and with it `localStorage` — stays stable across restarts. The window loads
`http://127.0.0.1:<port>/admin`, and `resolveHostUrl` collapses to `window.location.origin`. The IPC
grant for that runtime-known origin is registered at runtime, because Tauri's URL patterns cannot
express a port wildcard.

**Adoption of an already-running host requires proof.** The port-file port must equal the port the
bootstrapper itself persisted, *and* `GET /api/auth/status` on it must report `trusted: true`, which
only the loopback listener ever does. Anything else is ignored and the bootstrapper starts its own
host; there is no fallback to the public origin. Without that, a dev host publishing its port could
make the packaged app open a window against an origin that never authenticated it — a window with no
profiles in it.

### The host owns the OS autostart entry

The login item is written by the host (`IAutostartService`, with platform registrars for a macOS
LaunchAgent, an HKCU `Run` value, and an XDG `.desktop` entry) and is the single source of truth;
`openMinimized` is encoded in its command line as launch flags the bootstrapper reads from `argv`. The
setting is exposed as `GET`/`PUT /api/settings/autostart`, so the desktop shell and the browser-served
admin UI configure it through the same path.

This is a deliberate layering exception — the host writes OS state for the shell — justified because
the host is the single settings authority and the only process every admin UI can reach. The
bootstrapper passes its own executable path via `MACRODECK_SHELL_EXECUTABLE`; without it autostart
reports `supported: false`, and a startup pass rewrites an entry whose path went stale after an update.

### The bootstrapper posts OS notifications

It posts them from its own process through `notify-rust`, with the bundle identifier and product name
attached. The transport is a long poll on the trusted loopback API
(`GET /api/host/shell-notifications` plus a result post), because the window may be closed when a
notification fires. **Delivery is confirmed, not handed over**: the host falls back to its own platform
helper when the bootstrapper reports failure or stays silent past a short lease — branding is the
improvement, the notification arriving is the requirement. The bootstrapper claims notifications only
when it can brand them, so a development build leaves the host's helpers in charge rather than posting
as "Terminal".

## Consequences

- The shell drops the embedded Chromium: tens of MB instead of hundreds, and the host can run with no
  UI process attached.
- The UI is same-origin with the API and the WebSocket: no CORS, no bridge dependency for the base URL,
  and loopback trust applies to the page itself. The public listener serves LAN clients only, so its
  port can change without touching the desktop UI.
- The rendering engine is no longer pinned; the UI must work in three WebViews rather than one known
  Chromium version.
- Updates are cryptographically verified against a baked public key. Losing the private key permanently
  breaks the update path for installed apps, so it needs a durable backup.
- Autostart is configurable from any admin surface, and its failure mode is an API error instead of a
  silent no-op.
- A lost persisted loopback port changes the origin once and resets per-device `localStorage`
  preferences — accepted as rare and low-impact.
- A Rust toolchain joins the build prerequisites for the shell.
- The host and the bootstrapper now share a contract beyond startup: the notification poll shape, and
  the restart exit code from [ADR 0040](0040-public-listeners-and-tls.md), both have to ship on both
  sides of one release.
- On macOS `NSUserNotification` suppresses the banner while Macro Deck is frontmost, where the previous
  out-of-process helper always showed one.

## Alternatives considered

- **Keep Electron.** No benefit beyond inertia; the RAM and installer cost was the complaint.
- **A .NET tray app plus WebView (Photino, WebView2 WinForms).** Matches the host's stack but brings no
  bundler, updater or signing story — exactly the parts Electron was still carrying.
- **Host as entry point with a detached tray helper.** Updater and installer tooling assume the shell is
  the product root, and lifecycle ownership gets murky.
- **Keeping the autostart plugin and mirroring a host-side preference.** Needs polling or a push channel
  in the shell, and keeps two sources of truth.

## References

- [Issue #57](https://github.com/Macro-Deck-App/Macro-Deck/issues/57)
- [Releasing and packaging](../development/releasing.md)
