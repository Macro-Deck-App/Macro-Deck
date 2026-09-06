# Developer Documentation Site

`docs/` is the Astro/Starlight project published at <https://docs.macro-deck.app>. Public plugin, SDK, CLI, protocol, and compatibility documentation belongs here.

The site is organised as Introduction, SDK, CLI, Guides, Reference and Policies; the sidebar in [`astro.config.mjs`](astro.config.mjs) is the source of truth for that order. It documents extending Macro Deck from the outside - documentation for built-in integrations and for contributing one lives in [`engineering/`](../engineering/).

Repository-internal contributor documentation lives in [`engineering/`](../engineering/). Do not add plain internal Markdown beside the Starlight project.

## Content

Published pages live under `src/content/docs/`. Machine-readable protocol and manifest contracts live under `public/` and are served with the site.

Keep public documentation task-oriented. Explain how to use a contract and the constraints a developer must know; link to source or generated schemas instead of narrating implementation details.

## Build

```bash
npm ci
npm run build
npm run dev
```

Run these commands from `docs/`. The production workflow and Cloudflare configuration are the source of truth for deployment details.

Brand assets should be updated from the Macro Deck Branding repository rather than edited independently here.