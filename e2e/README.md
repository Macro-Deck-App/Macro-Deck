# Core E2E tests

The E2E suite runs Playwright against a staged production Macro Deck host rather than Angular development servers or mocked APIs.

Two host listeners are used deliberately:

- the loopback listener is used for the trusted Admin UI and first-run setup;
- the public listener is used for the Web Client so authentication and reconnect behavior exercise the same boundary as a browser on another device.

Every workflow run uses a fresh `MACRO_DECK_DATA_DIRECTORY` and dedicated ports. The tests run serially because the initial suite intentionally exercises one complete application lifecycle, including a real host restart and persisted state.

The host supervisor restarts the staged host only when it exits with Macro Deck's restart exit code. This lets restart tests use the production `/api/host/restart` path without adding a test-only lifecycle implementation.

## Run on Linux

Build the production UIs and stage the host first:

```bash
cd ui/angular
npm ci
npm run build:prod
cd ../..
./ci/scripts/stage-host.sh linux-x64 3.0.0-e2e Production
```

Start an isolated host:

```bash
export MACRO_DECK_HOST_BINARY="$PWD/ui/bootstrapper/host-publish/Macro Deck Host"
export MACRO_DECK_DATA_DIRECTORY="$(mktemp -d)"
export MACRODECK_HOST_PORT=5191
export MACRO_DECK_PORT=8192
export MACRO_DECK_SUPERVISOR_LOG="$PWD/macro-deck-e2e-supervisor.log"
bash ./e2e/scripts/host-supervisor.sh
```

In another shell, install Playwright and run the suite:

```bash
cd e2e
export MACRO_DECK_SUPERVISOR_LOG="$PWD/../macro-deck-e2e-supervisor.log"
npm install
npx playwright install chromium
npm test
```

The GitHub Actions workflow in `.github/workflows/e2e.yml` performs the same setup through `workflow_dispatch` and can run either all tests or the `@smoke` subset.

## Diagnostics

Failed tests retain screenshots, video, and Playwright traces. Before GitHub Actions uploads those files, `scripts/redact-playwright-artifacts.py` removes the E2E password, access/refresh cookies, bearer tokens, and JWT-shaped values from text content inside the report and trace archives. If redaction fails, the diagnostics upload is skipped.
