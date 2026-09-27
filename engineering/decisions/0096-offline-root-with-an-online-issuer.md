# ADR 0096: The root stays offline and signs one issuer, and revocation stops new installs

Status: Accepted

## Context

The root key pinned in every Macro Deck installation signed every package and registry certificate
directly, so the Platform held the root's private key online to issue them. Anyone who read it out of the
Platform could sign as any publisher, and it cannot be rotated without an app update. Once the one-level
verifier is widely installed, moving the Platform off the root needs an app update that understands the
alternative, so the verifier has to change before 3.0 stable.

The host already modelled revocation ([ADR 0044](0044-plugin-and-store-trust-enforcement.md)) but fed it
nothing, while the signed registry already publishes `revokedKeys` in `security.json`.

## Decision

**One intermediate level.** A certificate chains to the root directly, as before, or through exactly one
issuer certificate the root signed. The issuer carries only the `issuer` key usage and subject kind, uses
certificate `schemaVersion` 2 and names no issuer itself. A certificate it signs is `schemaVersion` 2,
names the issuer's `certificateId` and `rootKeyId`, carries exactly `package` or `registry`, and lies
inside the issuer's validity window. An issuer never signs a package or a registry manifest, and a
package or registry certificate never signs a certificate. A signed package carries `issuer.json` and
`issuer.sig` beside `certificate.json` and `certificate.sig`; the registry publishes issuer certificates
under `certificates/`. The root stays in the offline key-generation tool, which is the only place that
issues an issuer; the public CLI never does.

**Revocation is fed from the signed registry and refuses new bytes only.** A package whose certificate,
or whose certificate's issuer, is listed in the loaded registry's `revokedKeys` is refused at install and
update. Activation and launch of a version that is already installed do not consult revocation; the Store
marks such a plugin instead. Without a loaded registry the answer stays `Unavailable`, which does not
block. The registry refuses a snapshot signed by a revoked certificate or issuer, and a snapshot cannot
un-revoke its own signer.

## Consequences

- The Platform can take the root offline: a compromised issuer is revoked and replaced offline, without
  an app update and without touching the root.
- Macro Deck versions from before this change refuse an issuer-signed certificate. A registry certificate
  from an issuer takes the Store away from those hosts, so the Platform keeps the registry certificate
  root-signed, or waits for adoption, until older hosts no longer matter.
- Revoking an issuer that issued the current registry certificate would refuse the very snapshot that
  announces the revocation. The order is: publish a snapshot signed by a registry certificate from a new
  issuer (or the root), then revoke the old issuer.
- Revoking the Platform's issuer does not stop a single installed plugin; it stops new installs and
  updates of everything that issuer signed until the Platform re-signs under a new issuer. Stopping
  installed plugins would turn every issuer rotation into an outage on every machine, and plugins that
  launch before the registry cache loads would escape it anyway.
- Icon packs and profile templates carry no publisher signature, so a revoked publisher certificate does
  not reach them; they stay authenticated by the registry alone.
- The certificate schemas published elsewhere, such as the Store registry's, must accept
  `schemaVersion` 2 before the Platform issues through an issuer.

## References

- [ADR 0042](0042-plugin-signing-and-trusted-publishing.md), [ADR 0044](0044-plugin-and-store-trust-enforcement.md)
- [Signing and install trust](../../docs/src/content/docs/policies/security.md)
