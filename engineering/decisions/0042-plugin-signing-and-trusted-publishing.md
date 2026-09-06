# ADR 0042: Signing is one shared library anchored to a pinned root, and the Portal signs Store artifacts

Status: Accepted

## Context

The manifest format has carried a `signature` object since the artifact format was defined, and the
canonical digest has defined the exact bytes a signature covers. Nothing produced or checked one: the
host shipped a shape-only verifier that never returned a valid verdict, and the pinned root public key
sat in the host's domain layer referenced by nothing but its own test. Signing existed only in an
offline tool, which has already issued certificates and signed artifacts — so the certificate document,
the detached signature document and the embedded signature block are formats in the field, not open
designs.

Three components must agree byte for byte about what a signature covers: the CLI, the Platform that
publishes, and the host that enforces. A second implementation of the canonical digest anywhere is a
signature that verifies in one component and fails in another.

The original custody model was that a creator holds a key pair and signs their own artifact. That makes
every plugin author a key custodian, puts a long-lived signing credential in whatever CI publishes the
plugin, and makes the identity behind a Store artifact only as strong as the weakest developer's secret
handling. It also cannot express what the Store actually needs to attest: that a specific artifact came
out of a specific repository and workflow. Nothing in the field depended on it — no plugin was published
through it and the host did not enforce signatures at all — so the formats were fixed while the custody
model was not.

## Decision

**One package, `MacroDeck.Signing`.** It owns the Ed25519 primitives, key file handling, the certificate
document and its chain verification, the embedded package signature, the canonical digest of every
signable format, and registry-signature verification. It is published as a NuGet package because the
Platform is a separate repository, which makes its public surface a compatibility commitment. Ed25519
comes from NSec: .NET ships no in-box primitive, and NSec issued the certificates already in the field.

**The canonical digest is not re-implemented.** `MacroDeck.Signing` calls the packaging library's digest
function, and a pinned vector suite fixes its output so a future re-implementation — here, in the
Platform, or in another language — can be checked against it.

**The trust anchor lives next to the code that verifies against it**, not in the host's domain layer.
`--root-public` overrides it only for testing, and both `sign` and `verify` state loudly that such a
result is not anchored to the Macro Deck root.

**Every signable artifact carries its own signature and certificate; there is no sidecar.** All four
signable formats are ZIP archives with a JSON manifest, so each declares its files with digests and
sizes, carries a `signature` object, and carries `certificate.json` and `certificate.sig` at its root.
The certificate material is excluded from the declared file list and from the canonical digest: it
authenticates itself through the root signature over its exact bytes, so substituting it produces a
signature that no longer verifies. A detached document splits an artifact into two files that can be
separated, and none of these formats needed it.

**Each format has its own canonical digest** in one grammar, covering the package identity and the
declared file list, never the manifest's JSON encoding — so reformatting a manifest does not invalidate a
signature while adding a file or repointing an entrypoint does.

**Certificate validity is evaluated at the signature's timestamp**, not at verification time, so a
package signed while its certificate was valid stays verifiable after it expires. Stopping such a package
is revocation's job.

**Expected failures are results, not exceptions**, matching the manifest and artifact readers: an invalid
signature is a verdict a caller renders, not control flow.

### The Creator Portal is the only component that signs Store artifacts

Signing happens server-side with keys that exist only in Macro Deck infrastructure; certificate issuance
and revocation stay there too.

**Publishing is Trusted Publishing.** A plugin's CI workflow authenticates to the Portal with the
short-lived workload identity its platform issues, which attests the repository and workflow that is
running. The Portal verifies that the workflow is a trusted publisher for that plugin and verifies the
run's provenance before it signs. A submitted artifact is unsigned; the Portal returns and distributes
the signed one.

**Plugin developers and their CI never hold signing material** — no keys, no certificates, no signing
credentials, and no manual artifact upload as a publishing path.

**The public CLI signs and verifies; it never issues trust.** Root key generation, certificate issuance,
registry-service keys, registry signing and revocation administration stay in offline tooling and the
Portal. `keygen` produces a creator key pair and nothing else, and `sign` remains supported for artifacts
distributed outside the Store and for Macro Deck's own infrastructure — documented as what it is rather
than as a step towards publishing.

## Consequences

- Changing the canonical digest layout invalidates every signature ever issued. The vector suite makes
  that a loudly failing test rather than a silent field failure.
- The certificate wire format is published as a schema and constrained by what the offline tool already
  issued; changing it requires re-issuing certificates. Rotating the root is a code change plus a
  release, and `rootKeyId` exists so the old and new can be told apart.
- A signed artifact carries two archive entries older readers do not know about, and the plugin reader
  rejects undeclared files — so the reader change shipped together with signing, while nothing signs yet.
- The icon-pack and portable manifests gained a declared file list. It is written on export and additive,
  so an older Macro Deck still imports a newer archive; an artifact exported before this cannot be signed
  and has to be re-exported.
- **A password-protected portable export is not signable at all**: the payload does not exist until after
  the manifest bytes are used as encryption associated data, so there is nothing for a digest to cover.
- The library never consults revocation data. Every successful verification says so, and a caller that
  needs revocation must apply the feed itself.
- `signedAt` is not covered by any canonical digest, so a key holder can rewrite it without breaking the
  signature. Evaluating validity at `signedAt` is therefore advisory against a key holder who controls
  the timestamp; revocation is the control that actually stops a compromised key.
- Publisher identity becomes something the Portal verified rather than something a manifest asserts. The
  `publisher` block stays informational and must not be presented as verified attribution.
- A compromised developer machine or CI secret store cannot produce a signature chaining to the Macro
  Deck root, because there is nothing there to compromise. The blast radius moves onto the Portal's own
  key custody and trusted-publisher configuration.
- The concrete publishing interface is not specified here and does not exist yet; the public
  documentation describes the model rather than inventing an interface that would have to be corrected.

## References

- [Issue #604](https://github.com/Macro-Deck-App/Macro-Deck/issues/604),
  [Issue #636](https://github.com/Macro-Deck-App/Macro-Deck/issues/636)
- [ADR 0044](0044-plugin-and-store-trust-enforcement.md) — how the host enforces the result.
