# MacroDeck.Signing

Package signing for [Macro Deck](https://github.com/Macro-Deck-App/Macro-Deck): the Ed25519
primitives, the creator certificate chain, and the canonical digest every signable Macro Deck package
is signed over. One implementation, shared by the `macrodeck-plugin` CLI, the Macro Deck host and the
Macro Deck Platform, so an artifact signed by one of them verifies in the others.

What is in here:

- `MacroDeck.Signing.Keys` - Ed25519 key pairs, the base64 key files a signer holds, and
  `SigningMaterial`, which pairs a private key with the certificate it belongs to and refuses the
  pairing when it does not.
- `MacroDeck.Signing.Certificates` - the Macro Deck signing certificate and `SigningCertificateChain`,
  which verifies a certificate's **exact file bytes** against the Macro Deck root public key, requires
  an exclusive key usage, and evaluates the validity window at a caller-supplied instant.
- `MacroDeck.Signing.Packages` - `PackageSigner` and `PackageVerifier` over the five signable
  extensions, and the canonical digest of each format (`macro-deck-plugin/1`, `macro-deck-iconpack/1`,
  `macro-deck-portable/1`).
- `MacroDeck.Signing.Registry` - verification of a signed Store Registry manifest.
- `MacroDeckRootKey` - the pinned trust anchor. Verification only; the private half exists offline.

Signing keys are not a plugin developer's concern: artifacts published to the Macro Deck Store are signed
by the Creator Portal, server-side, and no signing key or certificate ever reaches a plugin author or a CI
workflow. See [Publishing to the Store](https://docs.macro-deck.app/guides/publishing/).

Every signable Macro Deck package carries its signature in its own manifest and its certificate as
`certificate.json` and `certificate.sig` at its archive root, so a signed artifact verifies on its own.
There are no detached signature files.

Two things this package deliberately does not do. It never issues trust: there is no root key
generation, no certificate issuance, no registry signing. And it never consults revocation data - a
successful verification proves the signature and the certificate chain at the time of signing, nothing
about whether that certificate is still trusted today.

Expected failures are results, not exceptions. A signature that does not verify is a verdict you
render, not an exception you catch.

See [the plugin development documentation](https://docs.macro-deck.app/introduction/getting-started/)
and the published schemas for the
[certificate](https://schemas.macro-deck.app/macrodeck-certificate-v1.schema.json) and the
[signature material](https://schemas.macro-deck.app/macrodeck-package-signature-v1.schema.json).

Licensed under Apache-2.0.
