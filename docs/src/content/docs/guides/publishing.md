---
title: Publishing to the Store
description: How a plugin reaches the Macro Deck Store - Trusted Publishing from your CI workflow to the Creator Portal, which verifies the publisher and signs the artifact server-side.
---

A plugin reaches the Macro Deck Store through **Trusted Publishing**: your CI workflow authenticates to
the Creator Portal with its own workload identity, the Portal verifies that the workflow is a trusted
publisher for that plugin, and the Portal signs the artifact itself. **You never generate, receive,
manage or hold a signing key or a certificate**, and neither does your CI workflow.

```text
Git repository
    -> trusted CI workflow
    -> Creator Portal
    -> publisher / provenance verification
    -> server-side signing
    -> publishing
```

**The concrete publishing interface is not available yet** - the endpoint your workflow calls, the token
exchange it performs and the workflow claims the Portal checks are not specified here. This page
describes the model the trust chain is built for, so that nothing you set up now points the wrong way.

## What you do

Prepare the plugin repository and the publishing workflow that will submit from it. Everything you run
locally is build and quality work, not trust work:

- [`validate --level publication`](/cli/validate/) the manifest before attempting an
  upload - this is the same readiness check the Portal applies, run locally where a failure is cheap.
- [`pack`](/cli/pack/) a Release build into a `.macroDeckPlugin` artifact.
- [`test`](/cli/test/) it against the [conformance suite](/sdk/conformance/).

None of this involves a key. An artifact you pack is unsigned, and that is what the Store expects to
receive.

### Publication-required metadata

Beyond the fields the host itself requires to install and run a plugin, publishing requires
`description`, `icon`, `license`, `repository`, `compatibility` and `publisher` (with `publisher.name`)
to be filled in - the full requirement table is in the
[manifest reference](/reference/manifest/#requirement-categories). `macrodeck-plugin build` and `pack`
already warn about any of these that are missing; `validate --level publication` is what turns those same
gaps into a hard failure (exit `SubjectInvalid`, 1), so you can check readiness in one command before
submitting anything:

```bash
macrodeck-plugin validate --level publication --manifest manifest.json
```

`publisher` identifies the Creator or Organization account that owns the listing, not free text. At
upload, the Creator Portal checks that `publisher.name` matches the authenticated Creator/Organization
account submitting the plugin, and that `publisher.id`, when present, is that account's id - this is a
Portal-side check, not something `validate` can verify locally, since local development contacts no
server and needs no Platform access.

## What your CI workflow does

The workflow authenticates to the Creator Portal using the trusted workload identity its platform issues
it - the same short-lived, workflow-scoped identity that proves *which repository and which workflow* is
running, not a secret you stored - and submits the packed artifact.

The workflow never signs anything. **No signing key, certificate or signing credential belongs in your
repository, in your CI configuration, or in your CI provider's secret store.** If a publishing setup asks
you for one, it is not this one.

## What the Creator Portal does

- Verifies that the submitting workflow is a trusted publisher for that plugin, and verifies the
  provenance of the workflow run itself.
- Signs the artifact **server-side**, with keys that exist only in Macro Deck infrastructure. The Creator
  Portal is the only component that signs Store artifacts.
- Publishes the signed artifact, once the submission has passed the Store's content review. Because the
  Store distributes the artifact it signed, an approved package cannot be silently replaced or modified
  afterwards without going through the process again.

Certificate issuance and revocation also live in the Creator Portal. Neither is something a plugin author
performs, and neither is something the `macrodeck-plugin` CLI can do.

There is no manual upload step in this flow. Submitting an artifact by hand is not how a plugin is
published or signed.

## Verifying a published artifact

[`macrodeck-plugin verify`](/cli/signing/#verify) checks a signed artifact's embedded signature and
certificate against the pinned Macro Deck root. It needs no network and no credentials, so it works as a
local check or as a CI gate on an artifact you downloaded - see [CI usage](/cli/ci/).

Note what a `valid` verdict is and is not: it is a cryptographic fact about the signature and certificate
chain at signing time, not a live trust decision, and revocation is not checked. See the
[security model](/policies/security/).

## Signing something yourself is a different thing

The CLI's [`keygen`](/cli/signing/#keygen) and [`sign`](/cli/signing/#sign) commands exist, and
they keep working - for artifacts distributed outside the Store, and for Macro Deck's own infrastructure.
They are **not** part of publishing to the Store, they are not a step in normal plugin development, and
running them is never a prerequisite for getting a plugin published. Signing a plugin locally does not
make it a Store artifact.

## See also

- [the plugin CLI](/cli/) - `validate`, `pack`, `test` and `verify` in full.
- [Security model](/policies/security/) - what a signature covers, what the host enforces today, and what
  it does not.
- [ADR 0042](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0042-plugin-signing-and-trusted-publishing.md) -
  why signing is server-side and why creator keys never reach a developer machine or a CI runner.
