# Companion version compatibility

The Companion app (Macro-Deck-App/Macro-Deck-Companion) and the host each name the oldest version of the other
they work with. The app reads the host's side on the anonymous `GET /api/auth/status` and decides; the host
never refuses an app for its version. Both fields are pinned by
[AuthPolicyMatrixTests](../../host/tests/MacroDeckHost.Tests.UnitTests/Auth/AuthPolicyMatrixTests.cs) and
[LockedHostGateTests](../../host/tests/MacroDeckHost.Tests.UnitTests/Api/LockedHostGateTests.cs).

## The fields

`GET /api/auth/status` adds, next to the fields the [Macro Deck 2 app](macro-deck-2-app.md) relies on:

- `version`: `HostVersion.Current`, the host's version without build metadata, for example `3.0.0-beta.15`.
- `minimumCompanionVersion`: `CompanionCompatibility.MinimumCompanionVersion`, the oldest Companion release
  this host works with, for example `26.1.1`.

Both are answered anonymously and while the key ring is locked, because an app too old may not get far enough to
sign in. An older app ignores them. The version was already public on the LAN through the mDNS TXT record; this
also names it to anyone who reaches the public listener, for example through a port forward.

## Ordering

Versions compare by Semantic Versioning 2.0 precedence: `3.1.0 > 3.1.0-beta.1 > 3.0.0 > 3.0.0-beta.13 >
3.0.0-beta.12`, with numeric pre-release identifiers compared as numbers and build metadata ignored. The app
treats a missing or unreadable value, and the fallback `0.0.0-dev`, as unknown, which refuses nothing.

## Raising the minimum

Raise `MinimumCompanionVersion` in the host release that stops working with older apps, and only to a Companion
version that is already released on every store. The app's own minimum host version lives in the Companion
repository, `MINIMUM_HOST_VERSION`, and its contract is `docs/integration/macro-deck-host.md` there.
