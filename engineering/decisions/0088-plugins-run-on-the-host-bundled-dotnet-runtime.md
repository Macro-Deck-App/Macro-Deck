# ADR 0088: Plugins run on a .NET runtime bundled with the host

Status: Proposed

## Context

Plugins are .NET 10 processes packaged per runtime identifier
([ADR 0029](0029-plugin-packaging-installation-and-supervision.md)). The template and
`macrodeck-plugin new` published them self-contained, so every plugin carried its own runtime: the
template plugin was about 114 MB (43 MB compressed) per platform self-contained, against about 1.9 MB
(0.7 MB compressed) framework-dependent.

Framework-dependent entrypoints already existed in the manifest, but nothing guaranteed a runtime to run
them. The host was a self-contained single-file bundle, so its own runtime was sealed inside the
executable and could not serve another process; a framework-dependent plugin only worked where the user
had installed .NET themselves.

## Decision

- **The packaged host ships a runtime and runs on it.** The host is published framework-dependent as a
  single file that only looks for a runtime in `runtime/` next to itself
  ([MacroDeckHost.csproj](../../host/src/MacroDeckHost/MacroDeckHost.csproj)). The ASP.NET Core runtime
  archive (which includes `Microsoft.NETCore.App`) is staged there by
  [stage-dotnet-runtime.sh](../../ci/scripts/stage-dotnet-runtime.sh), resolved by
  [resolve-dotnet-runtime.mjs](../../ci/scripts/resolve-dotnet-runtime.mjs): the host's own major at the
  building SDK's `BundledNETCoreAppPackageVersion`, so the host is tested and shipped on the same patch,
  downloaded from builds.dotnet.microsoft.com and verified against the sha512 in Microsoft's release
  metadata, failing closed.
- **Majors are only ever added.** [bundled-runtimes.json](../../ci/dotnet-runtime/bundled-runtimes.json)
  lists the channels to bundle. A new .NET major is appended beside the existing ones; a major that
  plugins may still target is never removed, and plugins are never rolled forward onto a major they did
  not ask for.
- **Bundled first, system as fallback.** A framework-dependent entrypoint is launched through the
  bundled muxer when it has every framework the plugin's `runtimeconfig.json` requires; otherwise a
  system `dotnet` that does is used, and one whose runtimes cannot be listed is launched anyway, as before
  ([DotnetMuxerLocator.cs](../../host/src/MacroDeckHost.Infrastructure/Plugins/DotnetMuxerLocator.cs)).
- **Framework-dependent is the default for new plugins.** `new` and the template generate
  framework-dependent entrypoints per runtime identifier. Self-contained stays fully supported, and an
  entrypoint without `runtime` still means self-contained.

## Consequences

- The installer grows by about 7 MB uncompressed (a 53 MB host plus a 112 MB runtime, against a 158 MB
  self-contained host on osx-arm64), and each framework-dependent plugin shrinks by about 40 MB
  compressed per platform. This amends the size rationale in
  [ADR 0015](0015-installation-and-update-delivery.md), whose AUR alternative is weighed against a
  149 MB self-contained host.
- Building the host now needs network access to builds.dotnet.microsoft.com (CI publish, local staging
  and e2e); a local cache keyed by sha512 covers repeat runs.
- Framework-dependent plugins appear as `dotnet` or ".NET Host" in Task Manager and Activity Monitor
  instead of under their own name.
- The Windows installer's lock probe also walks `host\runtime`, where plugin processes map runtime
  images from ([host-lock.nsh](../../ui/bootstrapper/installer/host-lock.nsh)).
- macOS: this amends the single-file host shape of
  issue #316 in the former Macro Deck 3 repository. Loose runtime files are safe because
  the host is staged under `Contents/Resources/host`
  ([tauri.macos.conf.json](../../ui/bootstrapper/tauri.macos.conf.json)), where non-Mach-O files are
  sealed as resources, and every Mach-O under it is signed by
  [sign-macos-host.sh](../../ci/signing/sign-macos-host.sh).
- An osx-x64 framework-dependent entrypoint that an arm64 host falls back to runs on the arm64 runtime;
  x64-only native libraries do not load, so such plugins must declare osx-arm64.
- Known risk: on Windows a damaged `runtime` folder may surface Microsoft's own "install .NET" dialog
  from the host launcher instead of a Macro Deck error.

## References

- [Manifest reference: Runtime](../../docs/src/content/docs/reference/manifest.md)
- [ADR 0026](0026-plugin-protocol-and-sdk-boundary.md) for the plugin compatibility commitment: the
  manifest format and `runtime` semantics are unchanged.
