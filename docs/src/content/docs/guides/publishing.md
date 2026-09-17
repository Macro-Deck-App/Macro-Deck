---
title: Publishing to the Store
description: How a plugin reaches the Macro Deck Store - checks before you publish, the release workflow, signing and updates.
---

A GitHub release runs the Macro Deck publishing workflow, which uploads an unsigned build to the
[Creator Portal](/creator-portal/). The Portal signs and publishes it after review - you never hold a
signing key. The step-by-step guide is [Publish a plugin](/creator-portal/publish-plugin/).

## Before you publish

Run each check on the build output, not the project directory:

- **Store metadata is complete** - `description`, `icon`, `license`, `repository`, `compatibility` and
  `publisher.name` are filled in ([manifest reference](/reference/manifest/#requirement-categories)):

  ```bash
  macrodeck-plugin validate --level publication --artifact ./artifacts/com.example.hello-deck-1.0.0-linux-x64.macroDeckPlugin
  ```

  Exit `1` means a field is missing. `build` and `pack` only warn about these fields; the Creator Portal
  applies the same check at upload.
- **The artifact passes conformance** ([conformance suite](/reference/conformance/)):

  ```bash
  macrodeck-plugin test --artifact ./artifacts/com.example.hello-deck-1.0.0-linux-x64.macroDeckPlugin
  ```

- **`id` equals the Project's Package ID** in the Creator Portal. The upload selects the Project by it.

## Publish with the release workflow

Save as `.github/workflows/release.yml`:

```yaml
name: Release

on:
  release:
    types: [published]

jobs:
  publish:
    uses: Macro-Deck-App/GitHub-Actions/.github/workflows/publish-plugin.yml@v1
    permissions:
      contents: read
      id-token: write
    with:
      version: ${{ github.event.release.tag_name }}
      source: src/HelloDeck
      changelog: ${{ github.event.release.body }}
```

How the trust chain works:

1. You connect your public repository to the Project in the Creator Portal.
2. The workflow authenticates with the short-lived token GitHub issues for the run. It proves which
   repository, commit and workflow built the package. It is not a secret you store.
3. The Portal accepts the build only from `publish-plugin.yml` and only for the connected repository.
4. After review, the Portal signs the package server-side and publishes it.

What the workflow must never do:

- **Sign anything.** No signing key, certificate or signing credential belongs in your repository, your CI
  configuration or your CI provider's secret store. If a publishing setup asks you for one, it is not this
  one.
- **Replace the release workflow with a manual upload.** A plugin package cannot be uploaded by hand.

See the [release workflow reference](/creator-portal/release-workflow/) for all inputs.

## Release a new version

```bash
gh release create v1.1.0 --notes "- Fix the greeting on light themes"
```

- The version comes from the release tag, not from `manifest.json`: the workflow writes it into the
  manifest. `v1.1.0` becomes `1.1.0` (SemVer 2.0).
- Every release goes through review again. Because the Store distributes the artifact the Portal
  signed, nobody can silently replace or modify an approved package.

## What the Store signs

```bash
macrodeck-plugin verify ./downloads/com.example.hello-deck-1.1.0-linux-x64.macroDeckPlugin
```

- The Creator Portal is the only component that signs Store artifacts. It signs server-side, with keys that
  exist only in Macro Deck infrastructure. It also issues and revokes certificates. Plugin authors do
  neither, and the `macrodeck-plugin` CLI cannot do either.
- An artifact you pack is unsigned. That is what the Store expects to receive.
- [`verify`](/cli/signing/#verify) checks a signed artifact against the pinned Macro Deck root. It works
  offline and needs no credentials. A `valid` verdict is a cryptographic fact about the signature at signing
  time, not a live trust decision, and it does not check revocation. See the
  [security model](/policies/security/).
- [`keygen`](/cli/signing/#keygen) and [`sign`](/cli/signing/#sign) are for artifacts distributed outside
  the Store and for Macro Deck's own infrastructure. They are not part of publishing. Signing a plugin
  locally does not make it a Store artifact.

## How updates reach users

```text
installed 1.0.0  <  Store 1.1.0  ->  shown as an update
```

- When the Store catalogue refreshes, Macro Deck compares the installed version with the latest Store
  version by SemVer. It offers an update only if the Store version is higher. It never offers an update
  when either version fails to parse.
- An update is only reported to the user. It is never installed unsigned on their behalf. Once a plugin is
  installed as signed, an unsigned update to it is refused, even if the user consents.
- The host verifies every package before install and again before every launch. Editing files after
  installation stops the plugin from loading.

## Removing a plugin from the Store

Unlist the Project in the Creator Portal: the plugin disappears from the Store listing, installed copies
keep working and the package id stays yours. See [Review and release](/creator-portal/review/#unlist).

A plugin removed by Macro Deck is listed as removed in the signed registry. Macro Deck then stops offering
it as an update and fails any new Store install of it.

## See also

- [Publish a plugin](/creator-portal/publish-plugin/) - the Creator Portal steps with screenshots.
- [CI and automation](/cli/ci/) - build, validate and test on pull requests.
- [`macrodeck-plugin validate`](/cli/validate/) - levels and problem codes.
- [Signing packages](/cli/signing/) - `verify`, and `keygen`/`sign` outside the Store.
- [Manifest reference](/reference/manifest/) - `publisher` and the publication fields.
- [Security model](/policies/security/) - what a signature covers and what the host enforces.
- [ADR 0042](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0042-plugin-signing-and-trusted-publishing.md) -
  why signing is server-side and why creator keys never reach a developer machine or a CI runner.
