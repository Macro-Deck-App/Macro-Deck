# ADR 0102: The plugin CLI renders previews with an embedded runtime and a local browser

Status: Accepted

## Context

A plugin author needs store images that show their widgets, regenerated in CI after a redesign. Only the UI runtime in `ui/runtime` turns a widget tree into pixels, and the plugin CLI is a .NET tool that is installed without Node and without a running Macro Deck.

## Decision

`macrodeck-plugin preview render` asks the plugin for its previews through the stub host and the `ui` capability, then draws each tree with a bundle of `ui/runtime` that is embedded in the CLI package. The bundle is built by `ui/scripts/build-preview-renderer.mjs` while the CLI project builds and is never checked in, so it always matches the runtime it was built with and branches that change the runtime cannot conflict over it. A build without `ui/node_modules` embeds no renderer, and packing the CLI fails then.

The pixels come from a Chrome, Chromium or Edge that is already installed, driven over the DevTools protocol with the platform's own WebSocket client. The CLI downloads no browser and takes no browser library as a dependency.

Only widget previews are drawn. Configuration views are rendered by the Angular app and stay out of scope until the runtime can draw them.

## Consequences

- Rendering needs a browser on the machine. A missing browser is a documented exit code, not a silent fallback.
- Output depends on the fonts installed on the machine, so images made on different machines can differ.
- Building the CLI with its renderer needs Node and `npm ci` in `ui`, also in the CI jobs that pack it. The CLI package grows by roughly 160 KB.
- The tool holds a small DevTools client. A browser library would replace it at the price of a dependency and a browser download.

## References

- Issue 1135
