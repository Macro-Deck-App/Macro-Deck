# Core E2E tests

The E2E suite runs Playwright against a staged production Macro Deck host rather than Angular development servers or mocked APIs.

Two host listeners are used deliberately:

- the loopback listener is used for the trusted Admin UI, first-run setup and test seeding, which the tests enter the way the desktop app does: with a loopback secret shared by the host and Playwright;
- the public listener is used for the Web Client so authentication and reconnect behavior exercise the same boundary as a browser on another device.

## Isolation

Every spec file starts its own host through [`tests/support/fixtures.js`](tests/support/fixtures.js) and stops it after its last test. [`tests/support/host.js`](tests/support/host.js) gives each host a fresh `MACRO_DECK_DATA_DIRECTORY`, its own loopback, public and HTTPS ports, its own `TMPDIR`/`TMP` (the single-instance check lives there) and its own loopback secret. It also plays the bootstrapper's supervisor role: a host that exits with Macro Deck's restart exit code is relaunched, so restart tests use the production `/api/host/restart` path.

Specs seed what they need through the host's own REST API ([`tests/support/seed.js`](tests/support/seed.js)) and only click the part they check. `core.spec.js` is the only spec that walks first-run setup and onboarding through the UI. Helpers in [`tests/support/pages.js`](tests/support/pages.js) prove that a page is live rather than merely loaded: `expectLivePush` changes a label over REST and waits for the page to paint it, and `pressWebTile` waits for the tile's UI session before pressing and checks that exactly one press left the page.

Specs do not depend on each other, so a single file runs on its own and `E2E_WORKERS=2` runs two files side by side.

## Run locally

`make test-e2e` (or `make test-e2e-smoke`) builds the UIs, stages the host and runs the suite in one go through [`scripts/run-local.sh`](scripts/run-local.sh), on Linux, macOS and Git Bash on Windows. `E2E_SKIP_BUILD=1` reuses the staged host.

By hand, build the production UIs and stage the host once:

```bash
cd ui
npm ci
npm run build
npm run build:web-client
cd ..
./ci/scripts/stage-host.sh linux-x64 3.0.0-e2e Production
```

On Windows without Git Bash, [`ci/scripts/stage-host.ps1`](../ci/scripts/stage-host.ps1) takes the same arguments (`pwsh ci/scripts/stage-host.ps1 win-x64 3.0.0-e2e Production`) and stages the runtime through [`stage-dotnet-runtime.ps1`](../ci/scripts/stage-dotnet-runtime.ps1), the counterpart of `stage-dotnet-runtime.sh`. A change to one staging script belongs in its counterpart too.

Then install Playwright and run the suite; it starts and stops its hosts itself:

```bash
cd e2e
npm install
npx playwright install chromium
npm test
```

`MACRO_DECK_HOST_BINARY` points at another staged host, `E2E_RUN_DIR` chooses where the hosts' data and logs go (the system temp directory by default).

The GitHub Actions workflow in `.github/workflows/e2e.yml` performs the same setup through `workflow_dispatch` and can run either all tests or the `@smoke` subset.

## Windows and macOS

The `@smoke` subset (setup, sign-in, a press and a host restart) also runs on Windows and macOS through [`.github/workflows/e2e-cross.yml`](../.github/workflows/e2e-cross.yml): on every push to `main`, nightly, on demand and before a release may be signed. It calls the same [`e2e-run.yml`](../.github/workflows/e2e-run.yml) job with its `os` input.

A host whose computer is locked refuses every client press with `HOST_LOCKED`, and a CI runner without an attended console can report itself locked. Press checks therefore read `GET /api/system/lock-state` first (`pressWebTileAndExpect` in [`tests/support/pages.js`](tests/support/pages.js)): unlocked, the press must take effect; locked, it must be refused and change nothing, and the test carries a `host-locked` annotation in the Playwright report.

## Diagnostics

Failed tests retain screenshots, video, and Playwright traces, and `test-results/junit.xml` lists every result. Each host keeps `macro-deck-host.log` and `macro-deck-host-supervisor.log` in its run directory; CI copies those two logs, never the data directories, which hold key material. Before GitHub Actions uploads anything, `scripts/redact-playwright-artifacts.py` removes the E2E password, every host's loopback secret (listed in the file `E2E_SECRETS_FILE` names) and session code, access/refresh/loopback cookies, bearer tokens, and JWT-shaped values from text content inside the report, the trace archives and the host logs. If redaction fails, the diagnostics upload is skipped.
