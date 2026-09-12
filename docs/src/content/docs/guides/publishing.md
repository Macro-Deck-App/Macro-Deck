---
title: Publishing to the Store
description: How a plugin reaches the Macro Deck Store - Trusted Publishing from your CI workflow to the Creator Portal, which verifies the publisher and signs the artifact server-side.
---

Your CI workflow submits an unsigned artifact to the Creator Portal, and the Portal signs and publishes it - you never hold a signing key.

:::caution[The publishing interface is not available yet]
The endpoint your workflow calls, the token exchange it performs and the workflow claims the Creator Portal
checks are not published yet. This page covers everything you can set up today and the trust model the
submission step will plug into. It does not guess the missing parts.
:::

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

- **`publisher` names your Creator or Organization account.** At upload the Portal checks that
  `publisher.name` matches the authenticated account and that `publisher.id`, if present, is that account's
  id. `validate` cannot check this locally because it never contacts a server.

## Publish with Trusted Publishing

Save as `.github/workflows/release.yml`. It uses the same build, validate and conformance steps as
[CI and automation](/cli/ci/) and runs when you push a version tag:

```yaml
name: Release

on:
  push:
    tags: ['v*']

jobs:
  build:
    strategy:
      fail-fast: false
      matrix:
        include:
          - os: windows-latest
            rid: win-x64
          - os: macos-latest
            rid: osx-arm64
          - os: ubuntu-latest
            rid: linux-x64
    runs-on: ${{ matrix.os }}
    defaults:
      run:
        shell: bash
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x

      - name: Install the CLI
        run: dotnet tool install --global MacroDeck.Plugin.Cli --prerelease

      - name: Unit tests
        run: dotnet test

      - name: Build the artifact
        run: macrodeck-plugin build --source src/HelloDeck --rid ${{ matrix.rid }} --output ./artifacts

      - name: Validate for publication
        run: macrodeck-plugin validate --level publication --artifact ./artifacts/*.macroDeckPlugin

      - name: Conformance
        run: macrodeck-plugin test --artifact ./artifacts/*.macroDeckPlugin

      - uses: actions/upload-artifact@v4
        with:
          name: plugin-${{ matrix.rid }}
          path: artifacts/*.macroDeckPlugin
```

The workflow stops at an unsigned, validated artifact. Once the Creator Portal publishes its interface,
a submit step goes after these steps. Until then there is nothing to add, and no key or secret goes in
your repository.

How the trust chain works:

1. You configure your repository and its workflow as the **trusted publisher** for your plugin in the
   Creator Portal. The configuration steps are not published yet.
2. The workflow authenticates with the short-lived workload identity its CI platform issues. That
   identity proves which repository and which workflow is running. It is not a secret you store.
3. The Portal checks that the workflow is a trusted publisher for that plugin and verifies the run's
   provenance.
4. The Portal signs the artifact server-side. The signed artifact is published once it passes the Store's
   content review.

What the workflow must never do:

- **Sign anything.** No signing key, certificate or signing credential belongs in your repository, your CI
  configuration or your CI provider's secret store. If a publishing setup asks you for one, it is not this
  one.
- **Replace Trusted Publishing with a manual upload.** Uploading an artifact by hand is not a way to
  publish or sign a plugin.

## Release a new version

```json
{
  "id": "com.example.hello-deck",
  "version": "1.1.0"
}
```

```bash
git commit -am "Release 1.1.0"
git tag v1.1.0
git push origin main v1.1.0
```

- The artifact version comes from the manifest's `version` (SemVer 2.0), not from the tag. Bump it before
  tagging. `build` names the artifact `<id>-<version>-<rid>.macroDeckPlugin`.
- Every release goes through the whole process again. Because the Store distributes the artifact the Portal
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

The signed registry lists removed packages. Macro Deck hides a removed plugin from the Store, stops offering
it as an update, and fails any new Store install of it. This applies to the plugin id as a whole. How an
author asks for a removal, and whether a single version can be withdrawn, is not published yet. There is no
CLI command for it.

## See also

- [CI and automation](/cli/ci/) - the same workflow for pull requests.
- [`macrodeck-plugin validate`](/cli/validate/) - levels and problem codes.
- [Signing packages](/cli/signing/) - `verify`, and `keygen`/`sign` outside the Store.
- [Manifest reference](/reference/manifest/) - `publisher` and the publication fields.
- [Security model](/policies/security/) - what a signature covers and what the host enforces.
- [ADR 0042](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0042-plugin-signing-and-trusted-publishing.md) -
  why signing is server-side and why creator keys never reach a developer machine or a CI runner.
