---
title: Actions
description: Expose actions with IActionDefinition and IActionExecutor - parameters, truthful ActionResults, dynamic options, UI-tree configuration, and how an action behaves inside an action flow.
---

An action is something a user can put on a button or into an action flow. You declare it with an
`IActionDefinition` (id, name, parameters) and run it with the `IActionExecutor` the definition creates.

## Quick start

```csharp
using System.Globalization;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;

public sealed class LightsIntegration : IPluginIntegration
{
	private readonly LightClient _client = new();

	public IReadOnlyList<IActionDefinition> Actions => [new SetBrightnessAction(_client)];

	// Other IPluginIntegration members omitted.
}

internal sealed class SetBrightnessAction(LightClient client) : IActionDefinition
{
	public string Id => "set-brightness";

	public LocalizedText Name => Strings.Actions.SetBrightness();

	public LocalizedText Description => Strings.Actions.SetBrightnessDescription();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Text("light", label: Strings.Parameters.Light(), required: true),
		ActionParameter.Slider("brightness", 0, 100, label: Strings.Parameters.Brightness(), defaultValue: 100)
	];

	public IActionExecutor CreateExecutor() => new Executor(client);

	private sealed class Executor(LightClient client) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			if (!client.IsConnected)
			{
				return ActionResult.Failed(ActionErrorCodes.NotConnected, Strings.Errors.BridgeOffline());
			}

			if (context.Parameters.GetValueOrDefault("light") is not string light || light.Length == 0)
			{
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter, Strings.Errors.NoLightSelected());
			}

			var brightness = Convert.ToDouble(context.Parameters.GetValueOrDefault("brightness") ?? 100,
				CultureInfo.InvariantCulture);

			await client.SetBrightnessAsync(light, brightness, context.CancellationToken);
			return ActionResult.Success();
		}
	}
}
```

The user gets a **Set brightness** action with a text field and a 0-100 slider, usable on any button and
in any action flow.

Things to know:

- **`Id` is persisted.** Profiles store it, so use stable lowercase kebab-case and never rename a
  released id. It is a local id - the host qualifies it with your plugin; never pass an
  `owner::local` value to an API that expects a local id.
- **`Actions` is read before `InitializeAsync`.** The list must be complete and side-effect free from
  construction on: building it must not connect, probe hardware or do other I/O.
- **The result is a claim.** `Success` says the operation completed. Anything else is `Failed` (or
  `Accepted`, see below) - never a quiet `Success`.
- **Honour `context.CancellationToken`.** It is cancelled when the running flow is aborted.

## Returning a result

```csharp
return ActionResult.Success();                                         // it happened
return ActionResult.Failed(ActionErrorCodes.NotFound, Strings.Errors.SceneGone()); // it did not
return ActionResult.Accepted(Strings.Status.WaitingForDevice());       // taken, not yet confirmed
```

| Result | Use when |
| --- | --- |
| `Success()` | The operation completed. `ActionResult.SucceededTask` is a cached `Task` for a synchronous executor. |
| `Failed(code, message)` | It did not complete. `code` is a stable, machine-readable reason; `message` is shown to the user as-is. |
| `Accepted(message)` | The provider took the request and its API **genuinely cannot confirm** completion. `message` says what it is waiting on. |

Do not return `Success` merely because a request was sent. If the provider can confirm completion, wait
for it (with a bound, see [long-running work](#long-running-work)) and report the real outcome.

The error message must read as an explanation, be localized, and carry no provider internals, tokens or
paths. Throwing is also fine - the flow engine records a failure and sanitizes the message - but a known
condition deserves its own code. Reuse `ActionErrorCodes` so a client can react the same way across
integrations:

| `ActionErrorCodes` | Meaning |
| --- | --- |
| `NotConfigured` | No usable configuration - no account, no instance. |
| `NotConnected` | Configured, but the provider is not reachable right now. |
| `PermissionDenied` | Missing scope, grant or OS permission. |
| `ProviderError` | The provider errored - unexpected response, broken call. |
| `ProviderRejected` | The provider understood the request and declined it. |
| `InvalidParameter` | A parameter is missing, malformed or unusable. |
| `NotFound` | The target does not exist. |
| `Timeout` | Completion could not be confirmed in time. |
| `Unavailable` | Not available here - wrong platform, unsupported provider version. |

A button's state provider can also report which state it expects next - see
[Button states](/features/button-states/#bridging-the-poll-delay).

## Declaring parameters

```csharp
public IReadOnlyList<ActionParameter> Parameters { get; } =
[
	ActionParameter.Choice("auth", [
		new ActionParameterOption { Value = "none", Label = Strings.Auth.None() },
		new ActionParameterOption { Value = "header", Label = Strings.Auth.Header() }
	], label: Strings.Parameters.Auth(), defaultValue: "none"),
	ActionParameter.Text("headerName", label: Strings.Parameters.HeaderName()).OnlyWhen("auth", "header"),
	ActionParameter.Secret("token", label: Strings.Parameters.Token()).OnlyWhen("auth", "header"),
	ActionParameter.Url("url", label: Strings.Parameters.Url(), required: true, autoPrefixHttps: true),
	ActionParameter.Duration("timeout", label: Strings.Parameters.Timeout(), defaultMilliseconds: 5000)
];
```

`ActionParameter` has a factory per editor: `Text`, `MultilineText`, `Number`, `Slider`, `Toggle`,
`Password`, `Secret`, `Choice`, `DynamicChoice`, `Autocomplete`, `MultiSelect`, `Color`, `File`,
`Folder`, `Hotkey`, `Duration`, `DateTime`, `Json`, `Code`, `KeyValue`, `Object`, `Array`, `IpAddress`,
`Url`, `Icon`, `Image`, `KeyboardSequence`, `KeyboardCombo` and `WidgetTarget`. The executor reads
values by `Name` from `context.Parameters`. The key names in `Hotkey`, `KeyboardCombo` and
`KeyboardSequence` values are listed under [Keyboard combos](/features/events/#keyboard-combos).
A secret the user picks for a `Secret` or `Password` parameter, also as a declared child of an `Object`
or the item of an `Array`, arrives in `context.Parameters` as plaintext; no other parameter ever receives
a decrypted secret. See [secret resolution in flows](/policies/compatibility/#secret-resolution-in-flows).
A `KeyboardSequence` value arrives as a JSON string. The host renders each string value inside it as a
template on its own, so a rendered value, such as event text, only ever becomes the content of that
string and never adds, removes or changes steps.

A `Color` parameter receives `#rrggbb`. The user can bind it to a
[Color variable](/features/variables/#color-variables); the host resolves it when the action runs and drops
any alpha, so you still get six digits, or an empty value when the variable is unavailable. Set the parameter's `AllowAlpha` to `true` when your action can use
transparency: the picker then offers opacity and the value may be `#rrggbbaa`. A Macro Deck release from
before `AllowAlpha` ignores it.

`OnlyWhen` is presentation only: a hidden parameter keeps what the user typed, is skipped by validation,
and **is still sent to the executor** - never infer anything from a field being hidden.

Set `Platforms` on the definition only when the capability does not exist on other platforms at all
(hibernation on macOS); the action is then neither listed nor executable there.

`Role` names a job the action does for Macro Deck itself, one of the `ActionRoles` values, and is `null`
for an ordinary action. A role fixes the parameters: `ActionRoles.LaunchApplication` takes a required
`path` and an optional `arguments`. When the user drops an application onto the deck, Macro Deck binds the
new button to a built-in action with that role; a plugin's action with the role is never chosen, so a
plugin never receives the dropped path. A Macro Deck release from before `Role` ignores it.

`RiskCategory` says what the action can do outside Macro Deck: `Scripts` (runs code), `Programs` (starts,
stops or opens programs, runs a command), `Input` (keystrokes, mouse or text), `Network` (requests to an
address the parameters choose), `Files` (opens, reads or writes files) or `Power` (shuts down, restarts,
suspends). The default is `None`. Macro Deck asks the user before an AI client connected over
[MCP](/reference/mcp/) runs an action that declares a category; the user can change that per provider.
A plugin's actions are asked about whatever they declare, so leaving it unset never lets an AI client run
your action unasked. Declare the category that fits best, so the user sees why Macro Deck asks.

```csharp
public sealed class RunCommandAction : IActionDefinition
{
	public ActionRiskCategory RiskCategory => ActionRiskCategory.Programs;
	// ...
}
```

A Macro Deck release from before `RiskCategory` ignores it.

`ActionRoles.RunScript` and `ActionRoles.RunRemoteScript` mark an action that runs a Macro Deck script, here
or on another instance. Such an action takes a required dynamic-choice `scriptId`; an option may carry the
metadata `scriptInputs` (the script's inputs as a JSON array) and `runsOnWidget` (`"true"`). The action
editor then adds an `input:<name>` parameter per declared input and, for `RunScript` only, a `widget`
target for a widget script. `RunRemoteScript` also takes an optional dynamic-choice `instance`, sent along
when the editor lists the scripts. Any action with one of these roles, a plugin's included, gets this editor.

### Letting the user search a long list

A `Choice` or `DynamicChoice` parameter draws a plain select. For a long list, call `AsSearchable()` on it
and the user can type into the field to filter the options by label:

```csharp
ActionParameter.DynamicChoice("counter", label: Strings.Parameters.Counter(), required: true).AsSearchable()
```

Typed text alone is never stored: the value is still one of the options, and the field shows the picked
option's label. Other parameter
types ignore it, and so does a Macro Deck release from before `Searchable`, which draws the plain select.
For a configuration view, see [Letting the user search a long list](/ui/views/configuration/#letting-the-user-search-a-long-list).

### Code parameters

`ActionParameter.Code(name, language)` draws a code editor. `language` picks its highlighting; use one of
the documented `UiCodeLanguages` values from `MacroDeck.Ui` (the same vocabulary as a
[code input](/ui/views/configuration/#code-input)). An editor that does not know the language shows plain text.

Set `VariableInsertFormat` to offer an **Insert variable** button: the editor inserts the format at the
cursor with `{name}` replaced by the picked variable's name.

```csharp
new ActionParameter
{
	Name = "script",
	Type = ActionParameterType.Code,
	Language = "autohotkey",
	Label = Strings.Parameters.Script(),
	VariableInsertFormat = "MacroDeck.GetVar(\"{name}\")"
}
```

Inserting only edits the code text. Macro Deck never renders a `Code` value as a template, so your
language reads the variable itself at run time. Without the format, and on a desktop release from before
`VariableInsertFormat`, the editor offers no insertion. Other parameter types ignore it.

### Rendering array items

An `Array` parameter's items reach the executor exactly as the user entered them. Call
`WithTemplatedItems()` to have Macro Deck render them first, like other templated parameters: a string
item is rendered as a template, so `--user={{ vars.user }}` arrives as `--user=Ada`, and an item bound to
a variable arrives as that variable's value. Other items are passed unchanged.

```csharp
ActionParameter.Array("arguments", ActionParameter.Text("argument"), label: Strings.Parameters.Arguments())
	.WithTemplatedItems()
```

A Macro Deck release from before `TemplatedItems` ignores it and passes the items as entered.

## What the executor gets

| `ActionExecutionContext` | |
| --- | --- |
| `Parameters` | Configured values, keyed by parameter name. |
| `CancellationToken` | Cancelled when the flow is aborted. |
| `OwnerWidgetId` | The widget whose flow is running, also inside a script that runs on a widget; `null` for automations, other scripts and other widget-less runs. |
| `ScriptId` | The script whose flow is running; for a nested **Run Script**, the called script. `null` outside a script, and on a host that predates it. |
| `AutomationId` | The automation whose flow is running; `null` outside one, inside a script it started, and on a host that predates it. |
| `OriginClientId` | The client that pressed, or `null` for a backend-initiated run. |
| `Interactions`, `Ui` | Ask the originating client a question or open a [modal](/ui/views/modal/). `null` when nobody is there to ask - handle it. |
| `CallDepth` | Script hops this run is nested behind. Pass it on, incremented, when handing work to another Macro Deck. |

To write the user's variables as this step sees them, script and automation locals included, pass
`UserVariableContext.From(context)`; see [Scopes](/features/variables/#scopes).

## Options that depend on provider state

```csharp
internal sealed class JoinChannelAction(DiscordClient client) : IDynamicOptionsActionDefinition
{
	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.DynamicChoice("guildId", label: Strings.Parameters.Server(), required: true),
		ActionParameter.DynamicChoice("channelId", label: Strings.Parameters.Channel(), required: true)
	];

	public async Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context, CancellationToken cancellationToken)
	{
		if (!client.IsConnected)
		{
			return new DynamicOptionsResult { Options = [], Error = Strings.Errors.DiscordNotRunning() };
		}

		if (context.ParameterName == "channelId")
		{
			if (context.CurrentParameters.GetValueOrDefault("guildId") is not string guildId)
			{
				return new DynamicOptionsResult { Options = [], Error = Strings.Errors.PickServerFirst() };
			}

			var channels = await client.GetChannelsAsync(guildId, cancellationToken);
			return new DynamicOptionsResult
			{
				Options = [.. channels.Select(c => new ActionParameterOption { Value = c.Id, Label = c.Name })],
				CacheSeconds = 15
			};
		}

		var guilds = await client.GetGuildsAsync(cancellationToken);
		return new DynamicOptionsResult
		{
			Options = [.. guilds.Select(g => new ActionParameterOption { Value = g.Id, Label = g.Name })]
		};
	}

	// Id, Name, Description, CreateExecutor omitted.
}
```

`DynamicChoice`, `Autocomplete` and `MultiSelect` without static options are filled by
`GetDynamicOptionsAsync`. `context.ParameterName` says which list is wanted, `CurrentParameters` holds the
rest of the (possibly unfinished) draft and `Filter` the typed text.

- **`Error` explains an empty list.** Set it to a localized message when you cannot answer right now
  (not connected, a prerequisite missing) instead of returning an unexplained empty list. A non-empty
  `Error` **replaces** the options in the editor. An empty list without an error is fine when there is
  legitimately nothing to offer.
- **`AllowsCustomValue`** lets the user type a value that is not in the list; **`CacheSeconds`** lets the
  editor reuse the answer.
- **Give an optional dynamic choice a `placeholder`** ("First available"): options are only fetched when
  the list opens, so until then an empty field would read as "still to choose".

## Options Macro Deck answers

Some lists are Macro Deck's own. Name one of the ids in `HostOptionsSources` (`MacroDeck.Sdk.Actions`) as the
parameter's `optionsSourceId`, and Macro Deck fills the list itself; `GetDynamicOptionsAsync` is not asked for
that parameter:

```csharp
ActionParameter.Autocomplete("variable",
	label: Strings.Parameters.SaveTo(),
	optionsSourceId: HostOptionsSources.UserVariables,
	required: true)
```

| Id | Lists | Option value |
| --- | --- | --- |
| `HostOptionsSources.Variables` | every variable, a typed name allowed | the canonical name |
| `HostOptionsSources.UserVariables` | the user's own variables, a typed name allowed | the canonical name |
| `HostOptionsSources.Devices` | the devices paired with Macro Deck | the device id |
| `HostOptionsSources.AndroidDevices` | the connected Android devices, after an empty "default device" option | the serial |

The ids are the strings Macro Deck has always answered, so a parameter declared with them works on every
Macro Deck version; a version that does not know an id offers no options for it. Widget pickers use
`WidgetOptionsSources` the same way.

## Configuring with a UI tree

```csharp
internal sealed class ToggleAction : IUiConfigurableActionDefinition
{
	public Task<IUiSession?> CreateConfigurationSessionAsync(
		ActionConfigurationRequest request, CancellationToken cancellationToken)
		=> Task.FromResult<IUiSession?>(new ToggleConfigSession(request.Parameters));

	// IActionDefinition members omitted - Parameters is still required.
}
```

Implement `IUiConfigurableActionDefinition` when a Macro Deck UI tree expresses the configuration better
than a flat list; the descriptor then reports `configuresWithUiTree`. `Parameters` stays authoritative: it
is what a client that cannot render a tree falls back to and what the host persists into. Returning `null`
declines and is not an error. See [Serving a configuration view](/ui/views/configuration/#from-an-action).

A tree's top-level inputs are saved as the parameters they are named after. A value the tree builds itself,
from nested fields or several inputs, is saved by wrapping the session in an `ActionConfigurationSession`
and reporting it for its declared parameter; see
[Reporting values the tree edits](/ui/views/configuration/#reporting-values-the-tree-edits).

## Inside an action flow

Action flows are user-authored block trees - action calls, conditions, delays, loops, scripts - stored and
run by the host's flow engine. Widgets, scripts and automations trigger them. Your integration
participates only through the actions it exposes; it does not own the flow graph or the scheduler, and
the persisted flow model is a host contract, not SDK surface.

What that means for an executor:

- **Failures propagate.** The flow engine does not treat every invoked action as successful: a `Failed`
  result (or an exception) is recorded and the originating client is told.
- **Cancellation stops the flow.** Pass `context.CancellationToken` to every await.
- **Widget-less runs are normal.** `OwnerWidgetId`, `Interactions` and `Ui` can all be `null`.

### Long-running work

```csharp
private async Task<ActionResult> ConfirmAsync(Func<MeldState, bool> reached, CancellationToken cancellationToken)
{
	var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
	while (!reached(_state))
	{
		var remaining = deadline - DateTime.UtcNow;
		if (remaining <= TimeSpan.Zero)
		{
			return ActionResult.Accepted();
		}

		try
		{
			await _stateChanged.WaitAsync(remaining, cancellationToken);
		}
		catch (TimeoutException)
		{
			break;
		}
	}

	return reached(_state) ? ActionResult.Success() : ActionResult.Accepted();
}
```

Never poll or wait unbounded in an executor. When you have to wait for confirmation, use a bounded
timeout and then return the truthful answer: `Failed(ActionErrorCodes.Timeout, ...)` when completion
should have been confirmable, `Accepted` when the provider simply does not report it. The built-in Meld
integration uses the pattern above after starting a stream.

## Over the plugin protocol

| Operation | SDK member |
| --- | --- |
| `describe` | `Actions` and each definition's parameters and capabilities |
| `execute` | `CreateExecutor().ExecuteAsync` |
| `options` | `IDynamicOptionsActionDefinition.GetDynamicOptionsAsync` |
| `state` | [`IStateProviderActionDefinition.GetActionStateAsync`](/features/button-states/) |
| `icon`, `icon.content` | [`IIconProviderActionDefinition`](/features/button-icons/) |

`VariableInsertFormat` and `TemplatedItems` travel as the optional `variableInsertFormat` and
`templatedItems` fields of a described parameter. A plugin that predates them sends neither and keeps the
old behaviour.

`PluginTestHarness.Actions` drives each operation the way the host does - see
[Testing](/features/testing/).

## See also

- [Button states](/features/button-states/) and [Button icons](/features/button-icons/)
- [Serving a configuration view](/ui/views/configuration/)
- [Localization](/features/localization/)
- [Capability parity](/reference/capability-parity/#failure-behavior)
