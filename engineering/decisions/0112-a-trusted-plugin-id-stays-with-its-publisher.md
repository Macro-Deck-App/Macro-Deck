# ADR 0112: A trusted plugin id stays with its publisher

Status: Accepted

## Context

[ADR 0044](0044-plugin-and-store-trust-enforcement.md) ratchets the trust tier of a plugin id but accepted
any certificate that chains to the root for any id. A second publisher with a valid certificate could sign a
package with someone else's plugin id, hand it to a user as a file or a URL, and the update replaced the
installed plugin and inherited its stored settings, accounts and secrets.

A certificate's `certificateId` changes whenever the Creator Portal rotates or re-issues it, and its issuer
changes when the Platform rotates its issuer ([ADR 0096](0096-offline-root-with-an-online-issuer.md)). The
certificate's `subject.kind` and `subject.id` name the publisher the Portal verified
([ADR 0042](0042-plugin-signing-and-trusted-publishing.md)) and survive both. A Creator Portal Project
transfer to another publisher is the one legitimate change of subject for an id.

## Decision

**The publisher is the certificate subject.** `PackageVerifyResult.Subject` exposes it, and each trust
record stores `kind` and `id` next to the certificate id. Kind is compared too, so a creator id and an
organization id cannot stand in for each other. The display name is never compared.

**Every trusted admission of an id must match the pin.** The pin is the most recently admitted `Trusted`
record of the plugin id, across every version and keyed on the records rather than on `current.json`, like
the tier. Unsigned records never select or clear it, and pruning a version folder keeps its `Trusted` record.
The check runs in the installer for every source kind, Force included; a mismatch is `SignerMismatch`.

**No install source moves the pin.** Store installs and automatic updates are compared like every
other source: the registry lists a digest and a display name for a release, not a signer, so the host has
nothing to tell a Project transfer from a release that should never have been listed.

**A record from before subjects were stored matches only its own certificate id.** That fails closed for a
rotated certificate until the plugin is reinstalled. Nothing published was signed when this shipped.

## Consequences

- A Creator Portal Project transfer of a signed plugin changes its subject, so installed copies refuse the
  new publisher's releases until they are uninstalled. Carrying the publisher subject in the signed registry
  release would let the host accept a declared transfer; that needs a registry format change.
- Pruning keeps Trusted records, so it no longer lowers the tier ADR 0044 ratchets.
- Uninstalling clears the pin while keeping the plugin's data by default, so switching publishers that way
  hands that data to the new package unless the user deletes it too.
- Launch-time backfill and upgrade of a record, which need local write access to the plugin directory and
  are outside the threat model, store the signer found on disk and can therefore move the pin.
- The pin is ordered by the wall-clock admission time; a clock set back can make an older admission win.
- Trusted records of pruned versions stay until the plugin is uninstalled.

## References

- [Signing and install trust](../../docs/src/content/docs/policies/security.md)
