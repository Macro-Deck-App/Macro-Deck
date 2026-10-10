# ADR 0106: The host owns the notification lifecycle and the Inbox is one of its parts

Status: Accepted

## Context

Plugins could only append fire-and-forget entries to the notification list. They could not show a toast or a
dialog, wait for a decision, return form values, or target a client, and the clients had no shared lifecycle for
a popup. A person answering is not bound by the plugin protocol's request deadline, a client id is chosen by
the client and proves nothing, and ADR 0075 deliberately gives a modal a live provider session, which a message
that must reach several clients at once cannot have.

## Decision

- One host singleton, the notification manager, owns every popup: creation, the single accepted response,
  timeouts, ownership, limits and the revision of each change. Each popup closes exactly once, and the first
  valid response wins under one lock. The host also applies its own maximum lifetime to every popup.
- The Inbox is a part of the manager, not a second system. The manager is the registered
  `IUserNotificationStore`, so every host producer already raises through it, and it forwards the Inbox
  operations to its Inbox store; a popup with the Inbox flag writes an entry there under the popup's id. The two lifecycles are
  independent: closing a popup never removes the entry and dismissing an entry never closes a popup.
- Source and owner are derived by the host from the authenticated caller. A client is identified by the device
  claim of its token, or by the administrator role for the desktop app, never by the client id it registers.
  A response from a connection outside the target is refused.
- Dialog content is a static widget-profile tree rendered by the existing renderer, with no session. The host
  derives what an input accepts from the tree and the component type alone, and returns the values of every
  input. Required fields and patterns are not part of that profile and stay the plugin's to check.
- The remote plugin protocol grows by additive operations on the `notifications` host api. The outcome reaches
  the plugin as a `ui` capability invoke, held while the plugin is detached and dropped when its session is
  pruned, like a modal result. A host that predates this answers unsupported.
- Clients receive the open popups when they register, treat that snapshot as authoritative, keep a short list of
  recently closed ids so a late event cannot revive a popup, and remove popups the host closed or that expired
  even while they are queued.

## Consequences

- Popups and results live in memory and do not survive a host restart. A plugin completes pending results as
  `HostUnavailable` when its session is lost.
- The Inbox is shown to administrator connections only, whatever the popup's target.
- Host producers still raise plain Inbox entries through the unchanged `IUserNotificationStore` interface. Only
  `INotificationManager` shows popups, so a producer such as a login failure becomes a toast by moving to it.
- The first-party UI wire grows by events and requests that older clients ignore.
- A form cannot declare a field as required; the plugin validates the returned values.

## References

- Issue #1286
- ADR 0026, ADR 0050, ADR 0062, ADR 0075
