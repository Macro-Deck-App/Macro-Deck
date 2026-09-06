---
title: Signing packages
description: 'keygen, sign and verify: creator key pairs, embedded package signatures, and what to do with a private key.'
---

Three commands exist for artifacts distributed **outside** the Macro Deck Store, and for Macro Deck's
own infrastructure: `keygen` produces a creator key pair, `sign` embeds a signature in a package, and
`verify` checks one.

**None of them is a step on the way to the Store.** Store artifacts are signed by the Creator Portal,
server-side, after it verifies the publishing workflow's identity - no plugin author ever generates,
receives or holds a signing key. See [Publishing to the Store](/guides/publishing/).

`verify` is the one of the three a normal plugin's CI has a use for; see [CI and automation](/cli/ci/).

## `keygen`

Generates a creator Ed25519 key pair for [`sign`](#sign). It never issues a certificate itself, and
nothing in the public CLI does: only the Creator Portal can turn a public key into a certificate the
Macro Deck root has signed.

| Option | Default | What it does |
| --- | --- | --- |
| `--output <dir>` | `.` | Directory to write the key pair into. |
| `--key-name <name>` | `macrodeck-creator` | Base file name for the key pair. |

Writes `<key-name>.public` and `<key-name>.private`, and never overwrites either file - an existing
`.public` or `.private` at the destination is an `output-exists` usage error before anything is written.
`root`, `macrodeck-root`, `registry` and `macrodeck-registry` are reserved key names: `keygen` refuses
them with `reserved-key-name`, because it can only ever produce a creator key pair, never a root or
registry key.

See [Private-key handling](#private-key-handling) before doing anything with the file `keygen` just
wrote.

Exit code is `Success` (0), or `UsageError` (2) for a reserved key name or an output file that already
exists.

## `sign`

**Not part of publishing to the Store**, for the same reason [`keygen`](#keygen) is not: the Creator
Portal signs Store artifacts server-side, and a locally signed artifact is not a Store artifact. Use this
for artifacts distributed outside the Store, or from Macro Deck's own infrastructure.

Signs a `.macroDeckPlugin`, `.macroDeckIconPack`, `.macroDeckProfile`, `.macroDeckFolder` or
`.macroDeckWidget` package with a creator certificate and private key. There is no detached signature
file: the signature is embedded in the artifact's own manifest, and the certificate is written to the
archive root as `certificate.json` and `certificate.sig`, so a signed artifact verifies entirely on its
own.

| Option | Default | What it does |
| --- | --- | --- |
| `package` (argument) | - | The package to sign. |
| `--output <path>` | - | **Required.** Where to write the signed artifact. Never overwritten if it already exists. |
| `--certificate <path>` | - | **Required.** Path to the signing certificate (`certificate.json`). |
| `--certificate-signature <path>` | - | **Required.** Path to the Macro Deck root's signature over the certificate (`certificate.sig`). |
| `--private-key <path>` | - | **Required.** Path to the base64-encoded private key `keygen` wrote. |
| `--root-public <path>` | pinned Macro Deck root | Verify the certificate against this root public key instead of the pinned Macro Deck root. Testing only - see [CI and automation](/cli/ci/). |

`sign` runs, in order: the same declared-file validation `validate --artifact` runs - every file
`files[]` declares, checked by SHA-256 and size against the real bytes; a chain verification of
`--certificate`/`--certificate-signature` against the pinned Macro Deck root (or `--root-public`), which
requires exclusive `package` key usage and a certificate that is valid right now; a check that the
certificate's public key matches `--private-key`; computation of the format's own canonical digest
(`macro-deck-plugin/1`, `macro-deck-iconpack/1` or `macro-deck-portable/1`); the signature itself; and a
write to `--output`. Before reporting success, `sign` re-verifies the artifact it just wrote with the
same check [`verify`](#verify) runs - a signed artifact that does not itself verify is a failure, not a
success.

`sign` refuses to replace an existing signature - a package that is already signed fails with
`already-signed` rather than being silently re-signed - and never overwrites `--output`.

Exit codes follow the [exit codes](/cli/#exit-codes) table, with the exact mapping from each failure:

| Exit code | Failures |
| --- | --- |
| `SubjectInvalid` (1) | A malformed, untrusted, wrong-purpose, not-yet-valid or expired certificate; a malformed private key, or one that does not match the certificate; a package that is already signed, or whose manifest is missing, malformed or too large, or whose declared files do not match the real bytes (a digest or size mismatch, an undeclared file, a missing declared file, or an unsafe entry). |
| `UsageError` (2) | An unsupported package format, or `--output` already exists. |
| `InputUnreadable` (3) | The package, certificate, certificate signature, private key or `--root-public` file could not be read. |
| `Cancelled` (4) | Ctrl-C. |
| `InternalError` (70) | Writing the signed artifact failed, or the artifact `sign` just wrote did not pass its own post-sign verification. |

## `verify`

Verifies a signed package's embedded signature and certificate against the pinned Macro Deck root (or
`--root-public`) with the same [`MacroDeck.Signing`](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/sdk/src/MacroDeck.Signing/README.md)
checks `sign` uses to self-verify: the certificate chain, its `package` key usage, its validity **at the
signature's own `signedAt`** rather than at verify time (so a package signed while its certificate was
valid stays verifiable after that certificate expires), the format's canonical digest, and every declared
file's SHA-256 and size. This is local cryptographic verification only - see
[CI and automation](/cli/ci/), and note the caveat every run prints.

| Option | Default | What it does |
| --- | --- | --- |
| `package` (argument) | - | The package to verify. |
| `--root-public <path>` | pinned Macro Deck root | Verify against this root public key instead of the pinned Macro Deck root. Testing only. |
| `--output <text\|json>` | `text` | How to render the result. |

Text output is one line naming the format and the signing certificate id on success, or `invalid:
<code>: <message>` on failure, always followed by a line stating that revocation was not checked.
`--output json` reports `{ valid, format, certificateId, rootAnchored, revocationChecked, problems[] }`;
`revocationChecked` is always `false`. `rootAnchored` is `false` whenever `--root-public` names a key
that is not the pinned Macro Deck root - `verify` also prints a `warning non-production-root` line in
that case, on both output formats.

Exit codes follow the same [exit codes](/cli/#exit-codes) table `sign` does, restricted to what a verify can
actually fail on:

| Exit code | Failures |
| --- | --- |
| `Success` (0) | The artifact is valid and root-anchored (or anchored to `--root-public`). |
| `SubjectInvalid` (1) | Unsigned, tampered, or invalid: a missing, malformed or algorithm-mismatched signature; a signature that does not verify; a certificate that is malformed, untrusted, wrong-purpose or was not valid at `signedAt`; a declared file whose digest or size does not match, an undeclared file, or a missing declared file; a malformed manifest. |
| `UsageError` (2) | The file's extension is not a signable package format. |
| `InputUnreadable` (3) | The package or `--root-public` file could not be read. |
| `Cancelled` (4) | Ctrl-C. |

`verify` never returns `InternalError` (70) - it writes nothing, so nothing it does can fail the way
`sign`'s write and self-verification step can.

## Private-key handling

This section applies only if you actually run [`keygen`](#keygen) and [`sign`](#sign) - that is, for
artifacts distributed outside the Store. **Publishing to the Store never puts a private key in your hands
or in your CI**, so there is nothing here to get wrong on that path.

The private key `keygen` writes is the only thing that can produce a valid signature under your
certificate, and `sign` never asks for anything else to prove who you are.

- **Never commit it.** Add `*.private` (or your chosen key name) to `.gitignore` before running
  `keygen`, not after.
- `keygen` writes it with file mode `0600` on Unix - owner read/write, nothing else. Windows has no
  in-band equivalent, so `keygen` prints a `restrictive-file-mode-unavailable` warning there and leaves
  securing the file to you (an NTFS ACL restricting it to your account).
- **A leaked private key means every artifact ever signed with it must be treated as untrusted**, and the
  certificate itself has to be revoked - a leaked key cannot be un-leaked, only replaced. Report the
  leak so the certificate can be revoked, generate a fresh key pair with `keygen`, and get a new
  certificate issued against the new public key.
- The CLI cannot re-issue or revoke anything itself: certificate issuance and revocation live in the
  Creator Portal, not in `macrodeck-plugin`. `keygen` only ever produces a key pair.
