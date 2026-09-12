---
title: Config and action flows
description: How configuration flows and action flows differ in Macro Deck 3.
---

Macro Deck has two unrelated concepts called flows:

| | Config flow | Action flow |
| --- | --- | --- |
| Purpose | Guide a user through integration setup | Execute user-authored automation blocks |
| Author | Integration/plugin developer | Macro Deck user |
| SDK entry point | `IConfigFlowProvider` / `IConfigFlow` | Actions exposed by integrations; the host owns the flow engine |
| Result | Persisted integration configuration | Side effects and a flow execution result |

## Configuration flows

A configuration-capable integration implements `IConfigFlowProvider`. Each setup session receives a fresh `IConfigFlow` instance.

`StartAsync` returns the initial step. `SubmitAsync` receives a step id plus the values submitted by the user and returns the next outcome.

### Outcomes

A flow returns one of four outcomes:

- `Step`: show another step.
- `Error`: redisplay a step with a general or field-specific validation error.
- `External`: open an external authorization URL and resume at a named step.
- `Complete`: finish setup and persist the collected configuration.

Expected validation failures should use `Error`, not exceptions.

### Steps and fields

`ConfigFlowStep` can contain a title, description, copyable values, instructions, links, normal fields, and advanced fields. Form fields reuse the action-parameter control vocabulary so the configuration UI and action editor present consistent controls.

Use advanced fields for escape hatches such as custom endpoints or an own OAuth client id rather than normal required setup.

Validate provider credentials or connectivity at the step where the user enters them when practical. Returning a clear field error is preferable to accepting invalid configuration and surfacing an integration issue later.

### Persisted values

Values submitted through secret/password fields are stored through the host's secret handling. A flow can also complete with additional plain or secret values that were never rendered as form fields, such as tokens obtained from an OAuth exchange.

At runtime, integrations read and update completed configuration entries through `IIntegrationContext.Config` / `IIntegrationConfig`.

A step's fields are `ActionParameter`s, so a step can ask the user to pick a widget with `ActionParameter.WidgetTarget`, rendered by the same picker action parameters use. A config flow belongs to an integration rather than a widget, so the `$self` sentinel is not offered there and the field yields a concrete widget id. Use the `WidgetTargetOptions` overload to restrict the choice to particular widget types.

### Entry metadata

The host may provide an `IConfigFlowEntryContext` in place of the base `IConfigFlowContext`. Its `EntryTitle` is the existing or host-requested configuration name; a null value means the flow must choose a title for a new entry. This lets one normal config-flow step ask for a name during creation while omitting that field when editing an existing entry.

Treat this context as optional and keep the flow functional when only `IConfigFlowContext` is available. `MacroDeck.Plugin.Hosting` forwards the metadata to out-of-process plugins, while older hosts and plugins simply omit it.

### OAuth

OAuth-capable flows use the host-provided OAuth session from the flow context. Macro Deck owns the callback endpoint and correlation state; the integration constructs the provider authorization URL and later exchanges the returned authorization code.

Do not start a second callback listener in the integration when the host OAuth session covers the provider flow.

## Action flows

Action flows are user-authored block trees stored by Macro Deck. Widgets, scripts, automations, and other triggers can execute them through the host flow engine.

An integration participates in an action flow only through the actions it exposes. It does not own the flow graph or execution scheduler.

Common blocks include action execution, conditions, delays, loops, scripts, and flow composition. The exact persisted model and engine implementation are host contracts rather than plugin SDK surface.

### Action results

An action result must describe what actually happened:

- Return success only when the operation completed successfully.
- Return a stable failure code for a known failure.
- Use accepted only when work was accepted but cannot yet be confirmed.

The flow engine propagates meaningful action failures rather than treating every invoked action as successful.

### Cancellation and long-running work

Actions and flow operations should honor cancellation. Avoid unbounded polling or waiting in an action executor. If a provider requires polling, use a bounded timeout and return a truthful failure when completion cannot be confirmed.

## Related documentation

- [Capabilities](/features/)
- [SDK reference](/reference/sdk-packages/)
- [Contributing an integration](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/development/contributing-integrations.md)
- [Macro Deck UI](/ui/)
