# Architecture Decisions

ADRs record project-wide choices that are difficult to reverse or that intentionally constrain future
work. Routine implementation details, provider-specific behaviour and bug fixes do not need one.

Use [0000-template.md](0000-template.md). Keep an ADR to the context that explains why the choice
exists, one clear decision, and the consequences that matter later. Do not transcribe implementation
code, protocol schemas, issue discussions or exhaustive option lists.

## Host and platform

- [0001 - Profiles are JSON files, written durably and recovered on read](0001-json-profiles-and-durable-writes.md)
- [0002 - Data roots and build channels](0002-platform-data-directories-and-build-channels.md)
- [0006 - A Tauri bootstrapper is the installed entry point](0006-tauri-bootstrapper-is-the-installed-entry-point.md)
- [0015 - Installation and update delivery are bounded by each platform's ownership model](0015-installation-and-update-delivery.md)
- [0024 - The log files are the log viewer's source of truth, and their format is a contract](0024-log-files-are-the-source-of-truth.md)
- [0078 - The macOS host pumps a CoreFoundation run loop on the main thread](0078-macos-host-pumps-a-core-foundation-run-loop-on-the-main-thread.md)

## Networking, trust and secrets

- [0003 - Loopback trust, token scopes, and device identity](0003-loopback-trust-token-scopes-and-device-identity.md)
- [0030 - Android USB connections terminate on the public listener](0030-android-usb-connections-over-adb.md)
- [0040 - Public listeners are a resolved endpoint set, secured by a per-installation local CA](0040-public-listeners-and-tls.md)
- [0047 - Secrets at rest, encrypted backups, and a staged boot-time restore](0047-secrets-backups-and-restore.md)
- [0054 - The Macro Deck Connect session is a host-owned refresh credential](0054-connect-session-is-a-host-owned-refresh-credential.md)
- [0062 - UI realtime is a ticketed JSON WebSocket, and it never blocks on a provider](0062-ui-realtime-transport.md)
- [0085 - The connect link is a binary record written as decimal digits](0085-compact-connect-link.md)
- [0086 - The host proves its identity to the companion app](0086-host-identity-key.md)
- [0087 - The Companion license is a signed bearer token the host keeps and hands out](0087-companion-license-token.md)

## Decks, flows and content

- [0004 - Capabilities are optional interfaces, discovered by registry](0004-capability-registries-and-declared-capabilities.md)
- [0009 - Portable archives, and migration from other applications](0009-portable-archives-and-migration.md)
- [0010 - One flow engine behind widget triggers, events and automations](0010-one-flow-engine-for-triggers-events-and-automations.md)
- [0012 - Scripts are reusable flows with declared inputs and an owning widget](0012-scripts-are-reusable-flows.md)
- [0022 - Icon identity is a locally computed hash, and a widget icon is a typed provider reference](0022-icon-identity-and-widget-icons.md)
- [0056 - Widget state is addressed by stable state id](0056-widget-state-is-addressed-by-stable-state-id.md)
- [0081 - Variables come from one provider catalog and carry attributes and a write capability](0081-variables-carry-attributes-and-a-write-capability.md)

## Plugins

- [0026 - The plugin boundary is a versioned JSON protocol with a DI-first .NET SDK over it](0026-plugin-protocol-and-sdk-boundary.md)
- [0028 - Plugin credentials are launch tokens, and development plugins pair interactively](0028-plugin-credentials-and-pairing.md)
- [0029 - Plugins are ZIP artifacts, installed atomically and supervised by the host](0029-plugin-packaging-installation-and-supervision.md)
- [0037 - SDK deprecation is declared metadata, confirmed by a build-time usage manifest](0037-sdk-deprecation-is-declared-metadata.md)
- [0042 - Signing is one shared library anchored to a pinned root, and the Portal signs Store artifacts](0042-plugin-signing-and-trusted-publishing.md)
- [0044 - The host enforces trust as a verdict, and the Store adds the signed registry chain](0044-plugin-and-store-trust-enforcement.md)
- [0088 - Plugins run on the .NET runtime bundled with the host](0088-plugins-run-on-the-host-bundled-dotnet-runtime.md)
- [0089 - A development build can temporarily take over an installed plugin](0089-a-development-build-can-temporarily-take-over-an-installed-plugin.md)
- [0090 - Invited testers install unreviewed test builds](0090-invited-testers-install-unreviewed-test-builds.md)
- [0091 - Enforced device revocation and long client access tokens](0091-enforced-device-revocation-and-long-client-access-tokens.md)
- [0092 - Plugins reach ADB through a permission-gated host API](0092-plugins-reach-adb-through-a-permission-gated-host-api.md)
- [0093 - Plugins and integrations talk over a host-brokered message channel](0093-plugins-and-integrations-talk-over-a-host-brokered-message-channel.md)

## Macro Deck UI

- [0038 - The UI model is a surface-agnostic versioned package, authored through a reactive DSL](0038-ui-model-and-declarative-dsl.md)
- [0041 - The web client's service worker caches the app shell and nothing else](0041-web-client-service-worker-caches-the-app-shell.md)
- [0050 - UI sessions are host-brokered, and a configuration tree renders a transaction it does not own](0050-ui-sessions-are-host-brokered.md)
- [0057 - Localized text is a reference resolved by whoever renders it](0057-localization-is-a-deferred-reader-resolved-reference.md)
- [0064 - Components are a registry over two namespaces](0064-components-are-a-registry-over-two-namespaces.md)
- [0065 - The component profile's authoring contracts](0065-the-component-profile-authoring-contracts.md)
- [0068 - Device sessions push full surface snapshots, and layouts are provider-registered descriptors](0068-device-sessions-and-layouts.md)
- [0075 - Widget types, folder views and modals are provider-registered and served through one provider](0075-provider-registered-surfaces.md)
- [0094 - Responsive layouts are chosen by the reader](0094-responsive-layouts-are-chosen-by-the-reader.md)
