---
title: Serving a configuration view
description: Rendering a config flow or an action's configuration as a Macro Deck UI tree beside the declared fields it never replaces.
---

Configuration is the first place Macro Deck renders your views in the shipped app. This page covers its
two plugin-facing entry points: an integration's config flow, and one configured instance of an action.
Both are opt-in and additive - you keep your existing config flow and your existing `Parameters` list
exactly as they are, and add a tree beside them.

The same surface carries two further entry points with their own rules, because neither has a declared
field list to fall back to: a folder's selected view (see [Folder views](/ui/views/folder-views/)) and a
widget (see [Configuring a widget](/ui/views/widget-configuration/)).

**A configuration tree renders a transaction it does not own.** It never completes a flow and never
persists a parameter. `SubmitAsync` remains the only way a config flow accepts values, and the ordinary
save path remains the only way an action instance is written. That is what keeps secret encryption, OAuth
and configuration replacement working unchanged - see
[ADR 0050](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0050-ui-sessions-are-host-brokered.md).

Two rules follow, and both matter:

- **Keep serving the declared fields.** Your `ConfigFlowStep.Fields` and your `IActionDefinition.Parameters`
  are still required. They are what a client that cannot render a tree falls back to, and for a config
  flow they are also how Macro Deck learns which submitted values are secret.
- **Name your top-level inputs after the fields they collect.** A top-level input node's id is the field
  key it submits, so the tree feeds the same transaction the declared fields do.

## A complete config tree

A two-field step - an API key and a poll interval - authored as a tree whose top-level input ids match
the declared field keys:

```csharp
var apiKey = new UiState<string>(string.Empty);
var pollSeconds = new UiState<int>(30);

var view = new UiConfigStack
{
    Key = "root",
    Children =
    [
        new UiStringInput
        {
            Key = "apiKey", // matches ConfigFlowStep.Fields' "apiKey" key
            Label = MyStrings.ApiKeyLabel(),
            Secret = true,
            Binding = Bind.To(apiKey),
            Required = true,
        },
        new UiNumberInput
        {
            Key = "pollSeconds", // matches the "pollSeconds" parameter/field key
            Label = MyStrings.PollIntervalLabel(),
            Min = 5,
            Max = 3600,
            Binding = Bind.To(pollSeconds),
        },
    ],
};
```

Because both input keys are top-level, their node ids are exactly `apiKey` and `pollSeconds` - the same
keys the declared `ConfigFlowStep.Fields` and `IActionDefinition.Parameters` use, so a submission through
the tree lands in the same transaction a submission through the fallback fields would.

## From a config flow

Implement `IUiConfigFlowProvider` on the integration and `IUiConfigFlow` on the flow. It goes on the flow
object itself so the tree's state and `SubmitAsync`'s state are one thing:

```csharp
public sealed class DemoIntegration : IPluginIntegration, IUiConfigFlowProvider
{
    public IConfigFlow CreateConfigFlow() => new DemoConfigFlow();
}

public sealed class DemoConfigFlow : IUiConfigFlow
{
    public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken) => ...;

    public Task<ConfigFlowResult> SubmitAsync(string stepId,
        IReadOnlyDictionary<string, object?> input,
        IConfigFlowContext context,
        CancellationToken cancellationToken) => ...;

    public Task<IUiSession?> CreateUiSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
        => Task.FromResult<IUiSession?>(new DemoConfigFlowSession(this));
}
```

Because Macro Deck relays your tree without inspecting it, it cannot tell which of a tree's values are
sensitive the way it can for a declared field. **A flow serving a tree classifies its own secrets** when
it completes, by returning them as secret config-flow values.

## From an action

Implement `IUiConfigurableActionDefinition`. One session is created per open configuration surface, so
several may be live at once for the same action - keep state on the session, never on the definition:

```csharp
public sealed class ToggleAction : IUiConfigurableActionDefinition
{
    public IReadOnlyList<ActionParameter> Parameters => [ActionParameter.Text("target"), ...];

    public Task<IUiSession?> CreateConfigurationSessionAsync(
        ActionConfigurationRequest request,
        CancellationToken cancellationToken)
        => Task.FromResult<IUiSession?>(new ToggleConfigSession(request.Parameters));
}
```

`request.Parameters` carries the values already stored on the instance being configured, keyed by
parameter name, so your tree can render them. `Secret`- and `Password`-typed parameters arrive **masked**
rather than at their real value: opening a configuration surface is a UI interaction, not an explicit
intent to reveal a stored secret. You receive the real value the ordinary way, when the user submits.

## Which representation a client renders

Returning `null` from either method declines, which is not an error - Macro Deck renders your declared
fields instead. The same is true of every other way the tree path can fail: a provider that times out,
disconnects, or trips a session limit lands the user on the working field list, never on an error.

The client chooses one representation, never a mix of both. It negotiates the UI model version locally,
before a session is opened, so a client that cannot render your tree costs you nothing - no session, no
slot.
