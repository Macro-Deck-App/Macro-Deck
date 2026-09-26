# Development Setup

Macro Deck 3 consists of a .NET host, the framework-free client packages under `ui/runtime/` and `ui/web-client/`, an Angular workspace, and a Tauri bootstrapper. For the component boundaries, see [architecture.md](../architecture.md).

## Prerequisites

- .NET SDK as pinned by [`global.json`](../../global.json).
- Node.js and npm.
- Stable Rust toolchain through rustup.
- Git.
- On Linux, the native packages required by Tauri/WebKitGTK.

Use the workspace-local Angular and Tauri tooling. A global Angular CLI installation is not required.

`ui/` is one npm workspace. Install its dependencies from there:

```bash
npm install
```

## Run the host

From the repository root:

```bash
dotnet build MacroDeck.slnx
dotnet run --project host/src/MacroDeckHost
```

Development builds keep their data in the repository-local `.data` directory unless a data-directory override is configured. Development and installed builds use separate identities and defaults so they can run side by side.

The development desktop UI reaches the trusted loopback listener. The web client reaches the public listener and therefore exercises the normal client authentication flow. See [architecture.md](../architecture.md) and the relevant ADRs for the trust model rather than duplicating the port and authentication implementation here.

The loopback listener requires a secret ([ADR 0098](../decisions/0098-loopback-trust-requires-a-per-launch-secret.md)). A host started without one generates it on every start and writes it to `config/loopback-secret` in its data directory; the path is in the host log. The `ng serve` proxy and a debug build of the bootstrapper read `.data/config/loopback-secret` in the repository, which is where it lands when the host runs from the repository root as above. Otherwise point `MACRODECK_LOOPBACK_SECRET_FILE` at the file, or export the same `MACRODECK_LOOPBACK_SECRET` for the host and the tools. For `curl`, send it as a header:

```bash
curl -H "X-MacroDeck-Loopback-Secret: $(cat .data/config/loopback-secret)" http://127.0.0.1:5191/api/profiles
```

Anyone who can read that file is admin on your development host, and so is anyone who reaches the `ng serve` port, which is why it must stay bound to `localhost`.

## Run the UI

The desktop UI is the Angular application. It consumes the built `shared` package, and the npm scripts handle the required build order.

From `ui/angular/`:

```bash
npm run start
```

This starts the desktop UI development server.

The web client is framework-free and has no development server: it is built, and the host serves the result. From `ui/web-client/`:

```bash
npm run build
```

The output in `dist/` reaches a running host through [`ci/scripts/stage-host.sh`](../../ci/scripts/stage-host.sh), which stages a published host with the client in its `wwwroot`. A development host started with `dotnet run` serves only its API and answers every other address with a placeholder page. The scripts in [`ui/web-client/package.json`](../../ui/web-client/package.json) cover the device-target builds and the compatibility passes; see [adding a Web Client target](web-client-targets.md).

To launch the Tauri bootstrapper against the development UI, run from `ui/bootstrapper/`:

```bash
npm run dev
```

The host must be running separately.

For the combined Angular/Tauri loop, run from `ui/`:

```bash
npm run dev
```

This does not start the .NET host.

## Clean state

Stop the development host and remove `.data/` when a clean local first run is required. Do not point development builds at production data unless the task specifically requires it.

## Next steps

Build and test commands are in [building-and-testing.md](building-and-testing.md). Repository conventions are in [coding-style.md](coding-style.md).