# ADR 0108: UI events carry their origin, and config entries are a plugin capability

Status: Accepted

## Context

ADR 0107 requires every built-in integration to reach the host only through the SDK, as a plugin would.
Several built-in widgets and their configuration could not: a press had no way to show a failure or open a
dialog on the client that pressed, every built-in session checked the host lock itself while plugin widget
sessions were not checked at all, and how configuration entries are prepared, reported and reloaded lived in
host-only adapters for single integrations. An SDK-only replacement has to work out of process with the
same guarantees, and a plugin compiled against an older SDK, or talking to an older host, must keep working.

## Decision

- Every client event reaches an `IUiSession` together with an `IUiEventOrigin`, through a default interface
  overload of `Dispatch` that forwards to the old one. The origin names the client and can report a
  failure there, shown as a failed `ActionExecutionStatusEvent`, or open one of the provider's own dialogs
  there. It acts only while that client is attached to the session, and stays usable after the handler
  returned. Out of process the calls are `ui`/`origin.report-failure` and `ui`/`origin.show-modal`; the host
  checks that the plugin serves the session and that the client is still attached to it, and a dialog's
  answer comes back as the existing `modal.result`. Against an older host the calls answer `false` or a
  cancellation.
- The host lock is enforced once, in the session broker, for the `widget` and `dialog` sessions of plugins
  and of non-system integrations served through the SDK: client events other than `reveal` are refused with
  `HOST_LOCKED`. Host widgets and dialogs keep their own policy.
- Taking part in storing configuration entries is the SDK interface `IConfigEntryLifecycle` and, out of
  process, the capability kind `config-entries`. The host drives it from one generic adapter, so a plugin
  and a built-in integration are served by the same code, and the host itself holds the variable names an
  entry reserves.

## Consequences

- A session that wants the origin overrides the new overload; one that does not changes nothing.
- A built-in session can drop its own lock check once it is served through the SDK. Plugin widgets and
  dialogs no longer react to presses while the host is locked, which they never promised to.
- The per-integration configuration adapters in the host are transitional and are removed as their
  integrations move to `IConfigEntryLifecycle`; no new one may be added.
- `config-entries` is a new kind within protocol major `3`; an older host rejects it non-fatally and stores
  entries as before.

## Update 2026-10-18: opening a link on the pressing client

A widget that lists links, such as a feed reader, needs to open the page a user tapped on the device the user
tapped it on, not on the computer Macro Deck runs on. The origin gained `OpenUrlAsync(Uri)`, out of process
`ui`/`origin.open-url`, as a default interface member that checks the URL and answers `false`, so an
`IUiEventOrigin` implemented before it keeps compiling and an older host answers `false` through
`CAPABILITY_UNSUPPORTED`.

Opening a page on a phone is more intrusive than a toast or a dialog, so it is narrower than the other
origin calls:

- Only an absolute `http` or `https` URL. The SDK throws for anything else, the host answers
  `INVALID_PAYLOAD` and normalises the URL again before sending it, and the client checks it once more.
- Keyed on the connection, not the client id: the broker records, per session, the time of the last
  discrete event (press, tap, change and the like) each connection sent. The link goes only to the newest
  connection of that client that sent one within 10 seconds and is still attached. Web client tabs share a
  client id, so a client-id group would have opened the page in every tab.
- Only to a connection that declared the `open-url` feature when it registered. The declaration is an
  optional second `RegisterClient` argument that an older host never reads; clients that do not send it
  (hardware decks, the current Companion app, older web clients) are answered `false`, and the provider
  falls back, for example to a dialog showing the link.
- One event opens one link: a successful call consumes the event's window, so a press cannot fan out
  into many tabs, in process (the desktop UI opens links without a popup blocker) or out of process.
- Out of process, also charged to the per-plugin callback throttle, like the other origin calls.
- `true` means handed to the client, not that a page opened: a browser may block the new tab, and the web
  client then shows the link to tap. The web client opens the page without an opener and with a
  `no-referrer` policy, so the site learns nothing about the deck.

The desktop UI declares the feature too and opens the link in the host's browser, because that is where its
user is. The client message is `UiOpenUrlEvent { url }`, sent to that one connection.

## References

- [ADR 0107](0107-built-in-integrations-are-built-as-extractable-plugins.md)
- [Serving a view](../../docs/src/content/docs/ui/views/sessions.md)
- [Setup flows](../../docs/src/content/docs/features/setup-flows.md)
