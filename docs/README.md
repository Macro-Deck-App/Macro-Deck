# Documentation Site

`docs/` is the Astro/Starlight project published at <https://docs.macro-deck.app>. It has three topics, each with its own sidebar in [`astro.config.mjs`](astro.config.mjs):

- **User guide** (`src/content/docs/guide/`): installing, setting up and using Macro Deck, for people who use it.
- **Creator Portal** (`src/content/docs/creator-portal/`): publishing plugins and icon packs to the Store through the Creator Portal.
- **Plugin development** (everything else): public plugin, SDK, CLI, protocol, and compatibility documentation. It documents extending Macro Deck from the outside - documentation for built-in integrations and for contributing one lives in [`engineering/`](../engineering/).

A new page must be listed in one topic's sidebar, or the build fails.

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

## User guide screenshots

User guide screenshots under `src/assets/guide/` are real captures of the desktop UI, taken from a disposable instance with a small example setup. Update them in the same change when the UI they show changes visibly.

1. **Build a Production-channel host**, so the UI shows no "Development build" badge: `dotnet build host/src/MacroDeckHost/MacroDeckHost.csproj -c Release -p:BuildChannel=Production -o <dir>`.
2. **Start it isolated** from any other instance on the machine: `MACRO_DECK_DATA_DIRECTORY` pointing at a fresh folder, `MACRODECK_HOST_PORT` and `MACRO_DECK_PORT` on free ports, and its own `TMPDIR`, because the single-instance check lives there.
3. **Serve the desktop UI** with `ng serve desktop-ui --proxy-config <file>` from `ui/angular/`, proxying `/api` and `/ws` (with `"ws": true`) to the host's loopback port. Build `ui/runtime` first.
4. **Create the account** on the welcome screen once. Then mark the onboarding tour done with `POST /api/settings/onboarding/complete` and clear notifications with `DELETE /api/notifications`.
5. **Build the example setup** through the REST API on the loopback port, which needs no login: profiles, folders, widgets, variables, automations, and integrations through their `config-flow` endpoints. Use neutral example data: no personal paths, names or devices.
6. **Capture** with `node screenshots/guide.mjs <shots.json> <outDir>`. It drives Playwright's `chrome-headless-shell` at 1280×800 with a device scale factor of 2. Each shot has a `name`, an optional `url` and `wait`, and optional `steps`: JavaScript run in the page before the capture, with `stepWait` between them.

```json
[
  { "name": "deck", "url": "http://localhost:4390/", "wait": 12000 },
  { "name": "variables", "stepWait": 3000, "steps": [
    "[...document.querySelectorAll('a,button')].find(e => e.textContent.trim() === 'Variables').click()"
  ] }
]
```

Things that cost time before:

- Deep links such as `/variables` land on the deck after a cold load. Load the app once, then navigate inside it by clicking the sidebar entries in later steps of the same run.
- The widget editor opens from `ng.getComponent(document.querySelector('app-deck-editor-grid')).widgetEdit.emit(widget)`, with `widget` taken from the component's `widgets`. The settings modal opens from `onNavAction('open-settings')` on `app-shell`.
- Widgets that load live data, such as Weather, need a long enough `wait`, or they are captured on their loading state.
- Do not turn on USB connections while an Android phone is attached: the disposable host creates an `adb reverse` tunnel on that phone.
- A parallel `npm run build` can leave `npm run dev` serving stale pages; restart the dev server.

Brand assets should be updated from the Macro Deck Branding repository rather than edited independently here.
## Creator Portal screenshots

Screenshots under `src/assets/creator-portal/` are captures of the Creator Portal in the Platform repository's mock mode (`docs/development/mock-mode.md` there), so no real account, GitHub App or storage is involved.

1. Run the API with `--MockMode=true` against a throwaway PostgreSQL database, and the portal with `ng serve developer-portal --ssl=false`.
2. Build the example state through the API with `Authorization: Bearer mock:mock-creator:`: create the Projects, connect `mock-creator/sample-plugin`, `POST /api/v1/mock/projects/{id}/builds` for a build, then create and submit the Version.
3. Capture with `node screenshots/guide.mjs`. Set `localStorage['macrodeck.mock-persona']` first, and hide what only mock mode shows in every shot: `md-creator-mock-bar` and the **Simulate release** button.
