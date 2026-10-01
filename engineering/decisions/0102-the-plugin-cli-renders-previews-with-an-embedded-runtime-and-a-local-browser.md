# ADR 0102: The plugin CLI renders previews with an embedded runtime and a local browser

Status: Accepted

## Context

A plugin author needs store images that show their widgets, regenerated in CI after a redesign. Only the UI runtime in `ui/runtime` turns a widget tree into pixels, and the plugin CLI is a .NET tool that is installed without Node and without a running Macro Deck.

## Decision

`macrodeck-plugin preview render` asks the plugin for its previews through the stub host and the `ui` capability, then draws each tree with a bundle of `ui/runtime` that is embedded in the CLI package. The bundle is a generated file, built by `ui/scripts/build-preview-renderer.mjs` and checked in, and CI fails when it is stale.

The pixels come from a Chrome, Chromium or Edge that is already installed, driven over the DevTools protocol with the platform's own WebSocket client. The CLI downloads no browser and takes no browser library as a dependency.

Only widget previews are drawn. Configuration views are rendered by the Angular app and stay out of scope until the runtime can draw them.

## Consequences

- Rendering needs a browser on the machine. A missing browser is a documented exit code, not a silent fallback.
- Output depends on the fonts installed on the machine, so images made on different machines can differ.
- A change to `ui/runtime` that affects widgets changes the checked-in bundle, and the CLI package grows by roughly 160 KB.
- The tool holds a small DevTools client. A browser library would replace it at the price of a dependency and a browser download.

## References

- Issue 1135
