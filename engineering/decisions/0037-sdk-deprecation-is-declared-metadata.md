# ADR 0037: SDK deprecation is declared metadata, confirmed by a build-time usage manifest

Status: Accepted

## Context

Nothing in the SDK was deprecated when this was written, and there was no way to deprecate anything. The
analyzer package already reported use of an `[Obsolete]` SDK member, but `[Obsolete]` carries one English
string and no structure — not when the API was deprecated, not which release removes it, not what to use
instead ([#418](https://github.com/Macro-Deck-App/Macro-Deck/issues/418)).

Meanwhile the manifest's SDK compatibility range was, in the installer's own words, recorded but not
checked: the host could not know which SDK a plugin was built against until it connected, and even then
learned only a version number.

That version number is the crux. Knowing a plugin was built against SDK 3.0.0 tells the host that
anything deprecated in 3.0.0 or earlier *might* be in use. It does not tell it that anything *is*. Those
two statements look alike in a UI and are not alike at all: showing "this plugin uses a removed API" to
someone whose plugin merely happens to be a version behind is a false alarm that trains users to ignore
the real one.

## Decision

**Deprecation is declared as structured metadata.** A deprecated API carries `[MacroDeckDeprecated]`
alongside a standard `[Obsolete]`. `[Obsolete]` stays because it is what the compiler, every IDE and every
third-party analyzer already understand; the Macro Deck attribute carries the deprecation version, the
planned removal version, the replacement and the migration guidance. The pairing, non-empty guidance and
a removal version later than the deprecation version are enforced at compile time, so a half-declared
deprecation cannot ship.

**Usage is confirmed by a build-time manifest, or it is not confirmed at all.** An incremental source
generator in the analyzer package records which deprecated APIs a compilation actually references and
emits them as an assembly attribute, which the hosting library reads at startup and sends in the session
request. Every finding the host reports is labelled with its evidence — `confirmed`, `negotiated`,
`inferred` or `unknown` — and only a manifest produces `confirmed`. An absent manifest yields `inferred`
findings worded as "may use", and an absent SDK block yields nothing at all.

**An empty manifest and a missing manifest are deliberately different**: empty is a positive statement
that the generator ran and found none, missing is the absence of a statement. Conflating them is exactly
the false confirmation this exists to prevent.

**Removal is gated on a documented, tested lifecycle.** Every deprecation has a registry entry pinned by a
test against the published deprecations policy. An API can only be removed at the version it declared, and
only by moving its entry from active to removed — where it stays forever, so a plugin built years ago
still gets a truthful answer about an API that no longer exists. An API still present past its declared
removal version is itself a build error.

## Consequences

- The host can say something specific and defensible about a plugin: which API, since when, until when,
  what to use instead, and how it knows. A plugin author sees the same finding under the same diagnostic
  id at build time, so nothing about it is a surprise at run time.
- The generator is the cost: it runs on every plugin compilation referencing the SDK. A plugin that does
  not reference the analyzer package gets no manifest and its findings degrade from `confirmed` to
  `inferred` — a worse experience for its users, but never a wrong one. The manifest contains only Macro
  Deck's own API names and is capped, so it is neither sensitive nor large.
- Three diagnostic ids exist only at run time, because nothing about a rejected capability or a failed
  negotiation is visible at compile time. They are reserved in the analyzer's compatibility band and must
  not be reused by a future rule — a coupling between two packages that a comment in each has to carry,
  since no test spans both.
- Nothing is deprecated today. The mechanism ships ahead of the first deprecation deliberately, so that
  the first one is a data change rather than a feature.

## Alternatives considered

- **An MSBuild target writing an embedded resource.** A target has no semantic model: it can re-read the
  SDK version and nothing more, which is precisely the inference this replaces. A source generator has the
  compiler's own view of which APIs are referenced, so the manifest and the warnings cannot disagree.

## References

- [Issue #418](https://github.com/Macro-Deck-App/Macro-Deck/issues/418)
- [Deprecations policy](https://docs.macro-deck.app/policies/deprecations/),
  [Analyzer reference](https://docs.macro-deck.app/reference/analyzers/)
