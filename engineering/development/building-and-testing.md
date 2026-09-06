# Building and Testing

This page is a command reference for local verification. CI implementation details belong in [`.github/workflows/ci.yml`](../../.github/workflows/ci.yml) and related workflow files rather than being duplicated here.

For first-time setup, see [setup.md](setup.md).

## .NET

Run from the repository root:

```bash
dotnet build MacroDeck.slnx -c Release -warnaserror
dotnet test MacroDeck.slnx -c Release
```

Run a specific project or filter when working on a focused area:

```bash
dotnet test host/tests/MacroDeckHost.Tests.UnitTests -c Release
dotnet test MacroDeck.slnx -c Release --filter "FullyQualifiedName~FolderCache"
```

Platform-specific test projects live beside the cross-platform host tests. Run the project for the operating system affected by a change when that platform is available.

Database schema changes require a new Evolve migration under `host/src/MacroDeckHost.Infrastructure/Persistence/DatabaseMigrations/`. The migration, not the EF entity definition, is the schema change.

C# formatting is also checked with the repository Rider/ReSharper settings. Use the repository cleanup/formatting configuration and ensure it leaves no diff. On a pull request from this repository, CI applies the cleanup itself and pushes the result to your branch, so pull before continuing to work on it. A fork's pull request cannot be written to, and there the job reports the diff and fails instead.

## Angular

Run from `ui/angular/`:

```bash
npm ci
npm test
npm run build:prod
```

The desktop UI consumes `shared` from its built output, and both consume the runtime package. Prefer the repository npm scripts because they build in the required order.

Focused commands for the individual projects are in `package.json`; use those rather than documenting the script file twice here.

Angular is zoneless, so the test patterns differ from a zoned workspace: there is no `fakeAsync`/`tick`/`waitForAsync` - use `async` tests with `await fixture.whenStable()`, `jasmine.clock()` or `TestBed.tick()` - and changing an `@Input` on an `OnPush` component needs `fixture.componentRef.setInput(...)`, since a direct field assignment does not mark the view dirty.

## Web client

The public web client is framework-free and builds outside the Angular workspace. Run from `ui/web-client/`:

```bash
npm test
npm run build
```

The build emits a down-levelled ES5 entry beside the modern one, and the device-target builds, the compatibility gate and its transforms have their own scripts in [`package.json`](../../ui/web-client/package.json). Run the compatibility scripts after the build, because they check what it just emitted.

The client shares its domain models, wire protocol and stylesheets with the Angular workspace through the runtime package in `ui/runtime/`, which has its own build, test and compatibility scripts.

## Tauri bootstrapper

Run from `ui/bootstrapper/`:

```bash
cargo fmt --check
cargo clippy --all-targets -- -D warnings
cargo test
```

Use `npm run dev` for the development bootstrapper and `npm run build` for a local package when the required host payload has been staged.

## NuGet packages

Package projects are built and packed through the normal .NET solution and release workflows. For a local package check:

```bash
dotnet pack <project> -c Release
```

Do not remove or relocate a package project's `README.md` without updating its package metadata. The repository packs these files as the NuGet package readme shown on nuget.org.

## What to verify

Verification should match the change. At minimum:

- Build affected code without warnings.
- Run tests covering the changed observable behaviour or contract.
- Run the formatter/linter for the affected language.
- For UI changes, verify the affected flow in the running application when practical.
- For public SDK, protocol, manifest, analyzer, package, or UI-model changes, run the relevant compatibility and conformance tests.

Do not add tests solely for coverage. Tests should protect a requirement, regression, public contract, state transition, or meaningful failure case.

## CI

Pull-request CI is defined by [`.github/workflows/ci.yml`](../../.github/workflows/ci.yml) and runs on every pull request against `main`, on a weekly schedule, and on demand. Release verification and packaging are defined by [`.github/workflows/build.yml`](../../.github/workflows/build.yml).

The end-to-end suite lives in [`.github/workflows/e2e-run.yml`](../../.github/workflows/e2e-run.yml) and is called from three places: pull-request CI, a release, and [`.github/workflows/e2e.yml`](../../.github/workflows/e2e.yml) for a manual run against a chosen suite or filter. It stages its own production host, so it checks the shipped artefact rather than the workflow that assembled it.

Treat the workflows as the source of truth for job names, runner versions, exact command flags, and dependencies. Update this document only when contributor-facing verification changes.