---
title: Signing packages
description: 'keygen, sign and verify: creator key pairs, embedded package signatures, and private-key handling.'
---

`keygen` creates a creator key pair, `sign` embeds a signature in a package, and `verify` checks one.

:::note
**None of these is a step towards the Store.** The Creator Portal signs Store artifacts server-side, and
no plugin author ever holds a signing key - see [Publishing to the Store](/guides/publishing/). `keygen`
and `sign` are for artifacts distributed outside the Store and for Macro Deck's own infrastructure.
`verify` is the only one a normal plugin's CI needs.
:::

## Examples

```bash
macrodeck-plugin verify ./downloads/com.example.hello-deck-1.0.0-linux-x64.macroDeckPlugin
```

```text
invalid: signature-missing: The manifest carries no 'signature' object.
Revocation was not checked; this is cryptographic verification only.
```

Check a downloaded artifact offline. This one is unsigned, so it fails with exit 1.

```bash
macrodeck-plugin verify ./artifacts/com.example.hello-deck-1.0.0-linux-x64.macroDeckPlugin --output json
```

```json
{
  "valid": false,
  "format": null,
  "certificateId": null,
  "rootAnchored": true,
  "revocationChecked": false,
  "problems": [
    {
      "code": "signature-missing",
      "message": "The manifest carries no 'signature' object."
    }
  ]
}
```

The same verdict as a document, for a pipeline that needs more than the exit code.

```bash
macrodeck-plugin keygen --output ~/.macrodeck/keys
```

```text
Public key:  ~/.macrodeck/keys/macrodeck-creator.public
Private key: ~/.macrodeck/keys/macrodeck-creator.private
Public key (base64): 3ay/DcXb90fbA+uH8jh5iMvx6ORF3AfUfOyA/0CPRPY=
Submit the public key above to the Creator Portal for certificate issuance - this command does not issue certificates.
```

Create a key pair outside your repository. Read [Private-key handling](#private-key-handling) first.

```bash
macrodeck-plugin sign ./artifacts/com.example.hello-deck-1.0.0-linux-x64.macroDeckPlugin \
  --output ./signed/com.example.hello-deck-1.0.0-linux-x64.macroDeckPlugin \
  --certificate certificate.json \
  --certificate-signature certificate.sig \
  --private-key ~/.macrodeck/keys/macrodeck-creator.private
```

Sign a package for distribution outside the Store, with a certificate the Creator Portal issued.

## `keygen`

Generates a creator Ed25519 key pair. It never issues a certificate; only the Creator Portal can turn a
public key into a certificate signed by the Macro Deck root.

| Option | Default | Description |
| --- | --- | --- |
| `--output <dir>` | `.` | Directory to write the key pair into. |
| `--key-name <name>` | `macrodeck-creator` | Base file name for the key pair. |

- Writes `<key-name>.public` and `<key-name>.private`.
- Never overwrites: an existing file at either path fails with `output-exists` before anything is
  written.
- `root`, `macrodeck-root`, `registry` and `macrodeck-registry` fail with `reserved-key-name`: `keygen`
  only ever makes creator keys.

```text
$ macrodeck-plugin keygen --key-name root
error reserved-key-name: 'root' is reserved for Macro Deck's own trust anchors and cannot be used as a creator key name.
```

Exit codes: 0 on success, 2 for `output-exists` or `reserved-key-name`.

## `sign`

Signs a `.macroDeckPlugin`, `.macroDeckIconPack`, `.macroDeckProfile`, `.macroDeckFolder` or
`.macroDeckWidget` package. There is no detached signature: the signature goes into the artifact's own
manifest, and the certificate into the archive root as `certificate.json` and `certificate.sig`, so the
signed artifact verifies on its own.

| Option | Default | Description |
| --- | --- | --- |
| `<package>` (argument) | - | The package to sign. |
| `--output <path>` | - | **Required.** Where to write the signed artifact; never overwritten. |
| `--certificate <path>` | - | **Required.** The signing certificate (`certificate.json`). |
| `--certificate-signature <path>` | - | **Required.** The root's signature over the certificate (`certificate.sig`). |
| `--private-key <path>` | - | **Required.** The base64-encoded private key `keygen` wrote. |
| `--root-public <path>` | pinned Macro Deck root | Verify the certificate against this root instead; testing only. |

`sign` runs these steps in order and stops at the first failure:

1. Declared-file validation, as `validate --artifact` does it: every file in `files[]` checked by
   SHA-256 and size.
2. Certificate chain verification against the pinned root (or `--root-public`). The certificate needs
   exclusive `package` key usage and must be valid now.
3. The certificate's public key must match `--private-key`.
4. The format's canonical digest (`macro-deck-plugin/1`, `macro-deck-iconpack/1` or
   `macro-deck-portable/1`), then the signature.
5. Write `--output`, then re-verify it exactly as [`verify`](#verify) would. An artifact that does not
   verify is a failure.

An already-signed package fails with `already-signed`; `sign` never replaces a signature.

```text
$ macrodeck-plugin sign app.macroDeckPlugin --output signed.macroDeckPlugin --certificate certificate.json ...
error certificate-untrusted: The certificate signature does not verify against the root key.
```

| Exit code | Failures |
| --- | --- |
| 1 | Certificate malformed, untrusted, wrong-purpose, not yet valid or expired. Private key malformed or not matching the certificate. Package already signed; manifest missing, malformed or too large; declared files not matching (digest or size mismatch, undeclared file, missing declared file, unsafe entry). |
| 2 | Unsupported package format, or `--output` already exists. |
| 3 | The package, certificate, certificate signature, private key or `--root-public` file could not be read. |
| 4 | Ctrl-C. |
| 70 | Writing the output failed, or the written artifact failed its own re-verification. |

## `verify`

Checks a signed package's embedded signature and certificate against the pinned Macro Deck root (or
`--root-public`), with the same
[`MacroDeck.Signing`](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/sdk/src/MacroDeck.Signing/README.md)
checks `sign` self-verifies with:

- the certificate chain and its `package` key usage;
- the certificate's validity **at the signature's `signedAt`**, not at verify time - a package signed
  while its certificate was valid still verifies after the certificate expires;
- the format's canonical digest, and every declared file's SHA-256 and size.

| Option | Default | Description |
| --- | --- | --- |
| `<package>` (argument) | - | The package to verify. |
| `--root-public <path>` | pinned Macro Deck root | Verify against this root public key instead; testing only. |
| `--output <text\|json>` | `text` | How to render the result. |

- **Text:** one line with the format and signing certificate id, or `invalid: <code>: <message>`.
- **JSON:** `{ valid, format, certificateId, rootAnchored, revocationChecked, problems[] }`.
- Every run ends by stating that revocation was not checked; `revocationChecked` is always `false`.
  This is local cryptographic verification only.
- With a `--root-public` that is not the pinned root, `rootAnchored` is `false` and a
  `warning non-production-root` line prints, in both formats.

| Exit code | When |
| --- | --- |
| 0 | Valid, and anchored to the pinned root (or to `--root-public`). |
| 1 | Unsigned, tampered or invalid: signature missing, malformed, algorithm-mismatched or not verifying; certificate malformed, untrusted, wrong-purpose or not valid at `signedAt`; declared files not matching; manifest malformed. |
| 2 | The extension is not a signable package format. |
| 3 | The package or the `--root-public` file could not be read. |
| 4 | Ctrl-C. |

`verify` writes nothing, so it never returns 70.

## Private-key handling

This applies only if you run `keygen` and `sign`. **Publishing to the Store never puts a private key in
your hands or your CI.**

The private key is the only thing that can produce a valid signature under your certificate; `sign` asks
for nothing else to prove who you are.

```bash
echo '*.private' >> .gitignore   # before running keygen, not after
```

- **Never commit it.** Ignore `*.private` (or your key name) before you run `keygen`.
- On Unix, `keygen` writes it with mode `0600` (owner read/write only). Windows has no in-band
  equivalent: `keygen` prints `restrictive-file-mode-unavailable`, and restricting the file with an NTFS
  ACL is up to you.
- **A leaked key taints every artifact ever signed with it**, and the certificate must be revoked. Report
  the leak so the certificate can be revoked, run `keygen` for a fresh pair, and get a new certificate
  for the new public key.
- The CLI cannot issue, re-issue or revoke certificates; that lives in the Creator Portal.

## See also

- [Publishing to the Store](/guides/publishing/) - how Store artifacts are signed.
- [CI and automation](/cli/ci/) - `verify` as a pipeline gate.
- [Security model](/policies/security/) - the trust model `verify` checks against.
- [Certificate schema](/schemas/macrodeck-certificate-v1.schema.json) and
  [package signature schema](/schemas/macrodeck-package-signature-v1.schema.json).
