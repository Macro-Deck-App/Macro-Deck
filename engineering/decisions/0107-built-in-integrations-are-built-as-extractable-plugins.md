# ADR 0107: Built-in integrations are built as extractable plugins

Status: Accepted

## Context

Built-in integrations are compiled into the host and discovered by reflection. They were meant to use the SDK
exactly as a plugin does, but the project reference to `MacroDeckHost.Application` and the lack of dependency
injection let host-only shortcuts grow around them:

- setter interfaces the host calls before initialization (sinks, gateways, stores, refresh signals) to hand an
  integration host services the SDK does not offer;
- per-integration hubs and widget-type constants in `Application`, because the widget UI that renders the
  integration's widgets lives in `MacroDeckHost.Widgets`, which cannot see the integration;
- widget UI that finds the integration instance at runtime and casts it to a host-defined interface;
- host-project adapters written for one specific integration.

Each shortcut works only in-process. Plugins get no equivalent, so the SDK never learns what a real
integration needs, and an integration cannot be moved out of the host or shipped on its own schedule.

## Decision

- Every built-in integration is built so that it could become an out-of-process plugin without much effort. It
  reaches the host only through the SDK: `IIntegrationContext`, the provider interfaces and the `MacroDeck.Ui`
  DSL.
- Everything an integration owns lives in its own folder in `MacroDeckHost.Integrations`: actions, config flow,
  state, widget types, widget and dialog UI served through `IUiProvider`, and assets. No part of it lives in
  `Application`, `Infrastructure`, `Widgets` or the host project, and no host code references it by its
  concrete type.
- `MacroDeckHost.Widgets` holds only shared base widgets: the host's own widget types and widgets that render an
  SDK capability any provider, a plugin included, can feed. A widget that serves one integration lives in that
  integration.
- When an integration needs something the SDK lacks, the SDK gains it as a public, documented, additive
  capability that also works over the plugin protocol. Each capability lands together with the first
  integration that uses it, so its shape is tested against a real consumer before it becomes a compatibility
  commitment.
- System integrations (`ISystemIntegration`) are host features expressed as integrations and are exempt.

## Consequences

- The existing shortcuts are debt. New work never extends or copies them, and they are removed integration by
  integration as the SDK capabilities they need become available.
- The SDK and the plugin protocol grow by the capabilities built-in integrations already rely on, so plugins
  gain them too. Each addition follows the compatibility rules of ADR 0026.
- When the migration is complete, `MacroDeckHost.Integrations` no longer references `MacroDeckHost.Application`
  or `MacroDeckHost.Domain`.
- Update 2026-10-13: done. `MacroDeckHost.Integrations` references no host project and the boundary baseline
  is empty. System integrations that need host services moved to `MacroDeckHost.Infrastructure`, and stateless
  platform code shared with the host moved to `MacroDeckHost.Platform`.

## References

- ADR 0026, ADR 0075, ADR 0092, ADR 0093
