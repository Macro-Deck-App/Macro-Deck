# ADR 0044: The host enforces trust as a verdict, and the Store adds the signed registry chain

Status: Accepted

## Context

[ADR 0042](0042-plugin-signing-and-trusted-publishing.md) built the signing primitives and closed by
recording that host enforcement was out of scope: the host accepted unsigned plugins and its shape-only
verifier never returned a valid verdict.

Two facts constrain enforcement. Nothing in the field is signed yet, so any rule refusing unsigned
packages outright refuses every plugin that exists. And the plugin directory is user-writable on every
supported platform, so a check performed once at installation says nothing about the bytes that will
actually be loaded.

The in-app Extension Store ([#517](https://github.com/Macro-Deck-App/Macro-Deck/issues/517)) added two
more. The published registry's release manifests reference a detached `*.signature.json` sidecar for the
formats that "cannot carry an embedded signature", and the published sample icon pack carries no
certificate at all — but nothing produces those sidecars, no schema describes them, and the shape
contradicts ADR 0042's embedded-signature rule outright. Implementing a verifier for them would mean
inventing a security-critical format with no counterpart on the publishing side. Meanwhile the registry
is already authenticated end to end, and that chain already covers artifact bytes: the pinned root signs
a certificate, a `registry`-usage certificate signs the exact bytes of the registry manifest, that
manifest carries the digest and size of every registry file, and each release manifest carries the
artifact's own digest and size.

## Decision

### Trust is a verdict, not a boolean

`PluginTrustVerdict` has eleven members: trusted, unsigned, malformed, invalid signature, content
mismatch, untrusted root, wrong certificate purpose, certificate not valid at signature time, revoked,
revocation unavailable, and verification unavailable. API and UI layers render the distinction; the
installer and supervisor branch on it.

`Trusted` is deliberately **not** enum member 0, and exactly one signing error maps to `Unsigned`. The
mapping's fallback arm yields `VerificationUnavailable`, never `Unsigned` and never `Trusted`, so
neither a default-initialised value nor an error member added to the shared library later can read as
trusted or as consentable.

### Everything unverifiable fails closed

An unreadable package and a signature algorithm this host does not understand both refuse the install,
the activation and the launch. This reverses the reasoning in the verifier ADR 0042 replaced, which
argued an unknown algorithm should be treated more leniently than no signature. It is costless today,
since only Ed25519 exists in the field, and it is what would break first when a second one ships.

### A signed installed tree is self-verifying, and is re-verified on every launch

The manifest, certificate and certificate signature are all extracted into the version directory, so the
installed copy carries everything needed to check itself: tampering with a payload breaks a declared
digest, tampering with the declared list breaks the canonical digest signature, and substituting the
certificate breaks the root signature. The host runs that check through the same implementation as the
archive path — a package that verified as an archive and failed once installed would be a hole rather
than a nuance — and install-time verification also runs over the staged *extracted* tree, which removes
the window in which the archive could be swapped between extraction and verification.

There is **no caching keyed on file size or modification time**: both are attacker-controlled, which
would make the check a formality against exactly the attacker it exists for. The cost is a SHA-256 pass
over the active version's declared files, paid on the launch path rather than in a startup sweep, so it
stays off the host's boot critical path and is never paid for a plugin that does not start.

**An integrity failure refuses the launch.** There is no untrusted-execution tier, because there is
nothing to demote into: the supervisor launches a plain OS process with the user's full privileges and
hands it the plugin protocol, so "running untrusted" would be a label rather than a control. The refusal
is terminal for the process lifetime rather than a restart-budget consumer, so reconciliation does not
re-hash a tampered plugin on every tick.

### The admitted tier is persisted and monotonic

The host persists only the tier a version was admitted at, in the application database rather than in the
user-writable plugin directory; without it, stripping the signature from an installed trusted plugin
would degrade it into the launchable unsigned state. It records intent rather than an unforgeable
attestation — the database is writable too — so it is backed by a one-time baseline: every plugin present
when enforcement first runs is grandfathered as unsigned, and after that marker exists an installed
plugin with no record is anomalous and refused. Deleting one row therefore fails closed.

An unsigned update over a version admitted as trusted is refused even with consent, keyed on the highest
tier ever admitted for that plugin id rather than on the currently active version, so a missing
`current.json` cannot reopen it.

### Unsigned consent is per install, and the Store needs Developer Mode

Only the `Unsigned` verdict is consentable, written as an equality rather than a fall-through — a package
whose signature does not verify is not an unsigned package, and that fall-through is what this exists to
prevent.

`LocalPath` and `Upload` are consentable independently of Developer Mode. `Url` — the Store — is
consentable **only while Developer Mode is on**. That exception exists because nothing published today is
signed, which made the Store's Install button unusable for exactly the plugins their authors are
publishing, while the same package installed fine if the user downloaded it and picked it manually. An
identical outcome reached by a longer route teaches people to route around the store rather than to want
signatures.

Developer mode is decided host-side; the wire flag is a request for consent, never an assertion that it
is permitted. Consent asked for while it is off fails the operation rather than installing or silently
dropping the flag. Consent covers one install: it travels in memory keyed on the operation id, is never
persisted, never survives a restart, and is not inherited by a retry.

### Store artifact integrity comes from the signed registry chain

**The detached sidecars are ignored.** The host never fetches, parses or verifies one. An artifact is
accepted when its bytes hash to the digest reached through root → certificate → registry manifest →
release manifest, checked while the bytes are written rather than by re-reading them afterwards.

**Plugins keep their own gate**: a store install runs through the installer, so the embedded signature,
the verdict and the monotonic tier all still apply. Registry authentication is added to that, not
substituted for it.

**Icon packs and profile templates are registry-authenticated only, and are never presented as verified.**
No API field and no UI affordance attributes a publisher to them. If a non-plugin artifact does carry an
embedded signature it is verified opportunistically and a failure refuses the install — a present
signature that does not verify is never treated as no signature.

**Trust is per origin.** The accepted sequence high-water mark, the installation records and the
certificate identity are keyed by registry origin, so a package id in a custom registry inherits nothing
from the official one.

**Two properties the signature chain does not provide are enforced separately.** A validly signed
snapshot stays valid forever, so an origin that stops updating could suppress revocations without failing
any check: snapshots are bounded by `signedAt` age and reported stale past it. And because the cache
lives in a user-writable directory it is re-verified on load *including its sequence*, since a
signature-and-digest check alone would still accept an older, validly signed snapshot dropped in by
whoever can write there.

**Revocation is modelled and enforced, but not fed.** `Revoked` refuses at install, activation and
launch. The shipped source answers `Unavailable` always, and `Unavailable` does not block: Macro Deck
publishes no revocation feed, and failing closed on its absence would refuse every plugin on every
machine, which is an outage rather than a security posture. Shipping a feed later is a change of data,
not of enforcement code.

**Development plugins are a separate trust path, not an exemption.** A self-registering plugin never
passes through the installer and the supervisor refuses to manage its process, so it never reaches the
launch gate; it is gated by possession of its credential and is never reported as trusted.

## Consequences

- Every plugin installed before enforcement is grandfathered as unsigned and keeps running, with the
  consent gate retroactively waived. The alternative refused every existing installation on a routine
  update, with no signed artifact to reinstall from.
- For an unsigned plugin the declared-digest check is corruption detection, not a security boundary: an
  attacker who can rewrite a binary can rewrite the manifest describing it. It runs because it is free
  and catches real corruption; the control for those plugins is the install-time decision, and the fix is
  signing.
- The Store's non-plugin trust level is exactly "the Macro Deck registry published this". The registry
  signing key is **online**, unlike the offline root, so its compromise yields arbitrary icon-pack and
  profile-template content with no second gate — and a profile archive imports scripts and secrets. That
  is the real cost of this decision and the reason plugins keep a separate, offline-rooted signature.
- The residual risk of the Developer Mode exception: a user who turned it on can install a store plugin
  nobody has vouched for, on one warning, over the network. It is bounded by Developer Mode being off by
  default and by the warning appearing only after the host has already refused the install once.
- A retry after a consented install does not carry consent, so the user sees the warning again. The
  alternative is a stored consent that outlives the decision.
- Both exceptions — grandfathering and store consent — are time-bound and should be removed once the
  Creator Portal signs what the Store serves.
- Anything published under the automation, folder or widget template prefixes is dropped while reading
  the registry: automations have no archive format, and folder and widget archives need an install target
  the registry cannot supply.
- A failed refresh keeps serving the last verified snapshot, so the Store degrades to stale rather than
  empty; a rollback attempt is refused outright.
- A local path is copied into staging before it is read, so a file-association install pays one extra full
  copy. It is the only way verification and extraction can be guaranteed to read the same bytes.
- The wire keeps its four coarse verdict strings and gains an additive fine-grained category, so an older
  client still receives a value it understands.

## References

- [Issue #517](https://github.com/Macro-Deck-App/Macro-Deck/issues/517),
  [Issue #610](https://github.com/Macro-Deck-App/Macro-Deck/issues/610)
- [ADR 0042](0042-plugin-signing-and-trusted-publishing.md)
