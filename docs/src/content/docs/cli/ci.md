---
title: CI and automation
description: 'Gating a pipeline on the CLI: build matrices, exit codes as the contract, and offline verification.'
---

The CLI is built to be a CI gate: every command reports its verdict through a documented
[exit code](/cli/#exit-codes), and the two commands a pipeline actually needs - [`build`](/cli/build/)
and [`test`](/cli/test/) - need no Macro Deck installation and no credentials.

## Gating a pipeline

`verify` is built to gate a pipeline, and of the two signing commands it is the only one a plugin's own CI
has any use for: a publishing workflow submits an unsigned artifact and never signs, so it needs no key
and no certificate (see [Publishing to the Store](/guides/publishing/)). The points below apply to `sign`
too, wherever it does run.

- **A build matrix runs one runtime identifier per runner**: `macrodeck-plugin build --rid ${{ matrix.rid }}
  --output ./artifacts`. Each job gates only its own target and produces its own single-platform artifact.
- The [exit codes](/cli/#exit-codes) are the contract - gate on the code, not on parsing text
  output. `verify --output json` gives you the same verdict as a structured document
  (`{ valid, format, certificateId, rootAnchored, revocationChecked, problems[] }`) when you need more
  than pass/fail.
- **`verify` needs no network.** The Macro Deck root public key is compiled into the tool, so verification
  runs offline, in an air-gapped runner, or in a build with no outbound access.
- `--root-public` exists for testing against a throwaway root in CI without touching the pinned Macro
  Deck root - never use it to verify a production release, and expect the `non-production-root` warning
  when you do.
- Every run - success or failure - states plainly that revocation was not checked. A `valid` verdict is
  a cryptographic fact about the signature and certificate chain at signing time, not a live trust
  decision; nothing in the CLI consults `security.json` or any other revocation feed.
