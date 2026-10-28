---
title: Setup flows
description: Guide users through connecting an integration with IConfigFlowProvider - steps, fields, validation errors, secrets, OAuth, reconfiguring an entry and optional settings.
---

A setup flow (config flow) walks the user through connecting your integration: a few form steps, then a
saved **config entry** your integration reads at runtime. Implement `IConfigFlowProvider` and return a
fresh `IConfigFlow` for each setup session.

Action flows - the automations users build out of actions - are a different thing; see
[Actions](/features/actions/).

## Quick start

```csharp
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Localization;

public sealed class MediaServerIntegration : IPluginIntegration, IConfigFlowProvider
{
	public IConfigFlow CreateConfigFlow() => new MediaServerConfigFlow();

	// IPluginIntegration members omitted.
}

public sealed class MediaServerConfigFlow : IConfigFlow
{
	public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken)
		=> Task.FromResult(ConfigFlowResult.Step(ConnectionStep()));

	public async Task<ConfigFlowResult> SubmitAsync(
		string stepId,
		IReadOnlyDictionary<string, object?> input,
		IConfigFlowContext context,
		CancellationToken cancellationToken)
	{
		var serverUrl = (input.GetValueOrDefault("server_url") as string ?? string.Empty).Trim();
		var apiKey = input.GetValueOrDefault("api_key") as string ?? string.Empty;

		if (!await MediaServerClient.CanConnectAsync(serverUrl, apiKey, cancellationToken))
		{
			return ConfigFlowResult.Error(ConnectionStep(), Strings.Setup.CannotConnect());
		}

		// Both form fields are persisted automatically; api_key is encrypted because it is a Secret field.
		return ConfigFlowResult.Complete("Media server");
	}

	private static ConfigFlowStep ConnectionStep() => new()
	{
		StepId = "connection",
		Title = Strings.Setup.ConnectionTitle(),
		Fields =
		[
			ActionParameter.Url("server_url", label: Strings.Setup.ServerUrl(),
				placeholder: "http://192.168.1.20:8096", required: true),
			ActionParameter.Secret("api_key", label: Strings.Setup.ApiKey(), required: true)
		]
	};
}
```

The user can now set up the integration from its page: they fill in the step, you check the connection,
and the host saves a config entry titled "Media server".

Things to know:

- **One `IConfigFlow` per session.** The host calls `CreateConfigFlow` for every setup session, so
  keeping per-session state in instance fields is fine.
- **`input` holds every value collected so far**, keyed by field name, with secrets already decrypted to
  plaintext for validation. Never log them or put them in an error message.
- **Expected failures are `Error`, not exceptions.** Validate credentials or connectivity on the step
  where the user types them - a field error now beats an [integration issue](/features/integration-issues/)
  later.
- **More than one entry is allowed by default.** Return `false` from `AllowsMultipleConfigurations` when
  a second one makes no sense (a single account).
- **A flow has to be completed before the integration runs.** Until the user saves an entry, the
  integration is off, shown as needing setup, and turning it on opens the flow. For a flow that only
  changes settings with defaults, see [Optional settings](#optional-settings).

## Outcomes

Every `StartAsync` and `SubmitAsync` returns a `ConfigFlowResult`:

| Factory | What the host does |
| --- | --- |
| `Step(step)` | Shows `step`. |
| `Error(step, message, fieldErrors)` | Shows `step` again with a general message and/or per-field messages keyed by field name. Values from this submit are discarded. |
| `External(url, resumeStepId)` | Opens `url` in the browser, waits for the redirect callback, then submits `resumeStepId`. See [OAuth](#oauth). |
| `Complete(title, values)` | Saves the entry: all collected form values plus the extra `values`. |
| `CompleteWithoutEntry()` | Closes the dialog and stores nothing. See [Completing without an entry](#completing-without-an-entry). |

## Multi-step flows

```csharp
public async Task<ConfigFlowResult> SubmitAsync(string stepId, IReadOnlyDictionary<string, object?> input,
	IConfigFlowContext context, CancellationToken cancellationToken)
	=> stepId switch
	{
		"connection" => await SubmitConnection(input, cancellationToken), // returns Step(InstanceStep(...))
		"instance" => SubmitInstance(input),                            // returns Complete(...)
		_ => ConfigFlowResult.Error(ConnectionStep(), Strings.Setup.UnknownStep())
	};

private static ConfigFlowStep InstanceStep(IReadOnlyList<BotInstance> instances) => new()
{
	StepId = "instance",
	Title = Strings.Setup.InstanceTitle(),
	Fields =
	[
		ActionParameter.Choice("instance_id",
			options: instances.Select(i => new ActionParameterOption { Value = i.Id, Label = i.Name }).ToList(),
			label: Strings.Setup.Instance(),
			required: true)
	]
};
```

Branch on `stepId`, and use an earlier step's answer to build the next one - here, the instances the
server reported. The built-in SinusBot integration works exactly like this.

The dialog offers a Back button once a step has been submitted, so `SubmitAsync` can arrive for an
earlier step id than the one you returned last, with the values the user corrected. Do not assume
steps arrive in order: dispatch on `stepId` and overwrite whatever state that step derives.
When the user goes back, the host drops everything the steps after that one collected, so `input`
and the saved entry only hold values from the path the user actually took. Flows that serve a
config UI tree (`IUiConfigFlow`) get no Back button, because their steps live in the tree.

## Steps and fields

```csharp
new ConfigFlowStep
{
	StepId = "credentials",
	Title = Strings.Setup.ConnectTitle(),
	Description = Strings.Setup.ConnectDescription(),
	Instructions =
	[
		new ConfigFlowInstruction { Text = Strings.Setup.CreateAppInstruction() },
		new ConfigFlowInstruction
		{
			Text = Strings.Setup.AddRedirectUriInstruction(),
			Values = [new ConfigFlowCopyValue { Label = Strings.Setup.RedirectUri(), Value = context.OAuth.RedirectUri }]
		}
	],
	Links = [new ConfigFlowLink { Label = Strings.Setup.Dashboard(), Url = "https://developer.example.com" }],
	Fields = [ActionParameter.Text("client_id", label: Strings.Setup.ClientId(), required: true)],
	AdvancedFields = [ActionParameter.Url("api_base", label: Strings.Setup.CustomEndpoint())]
};
```

| Member | Rendered as |
| --- | --- |
| `Title`, `Description` | Heading and the sentence introducing the step. |
| `Values` | `ConfigFlowCopyValue`s - labelled, monospaced values with a copy button. |
| `Instructions` | A numbered list; never number the text yourself. Each can carry its own copy values. |
| `Links` | Labelled links, such as a developer portal. |
| `Fields` | The form. |
| `AdvancedFields` | Hidden behind an "Advanced configuration" switch. Never `Required`. |

Fields are `ActionParameter`s, so they render with the same controls as action parameters: `Text`, `Url`,
`Number`, `Choice`, `Toggle`, `Secret` and the rest. Keep `AdvancedFields` for escape hatches - an own
OAuth client id, a non-default endpoint - not for normal setup. The section opens by itself when one of its
fields already has a value.

`OnlyWhen` works on step fields the way it does on [action parameters](/features/actions/#declaring-parameters):
a field shows only while the named field of the same step holds one of the listed values (compared
case-insensitively), and the dialog re-evaluates as the user picks, without a step round trip. A field whose
named field is still empty stays hidden.

```csharp
Fields =
[
	ActionParameter.Choice("brand", brands, label: Strings.Setup.Brand(), required: true),
	ActionParameter.Choice("razerModel", razerModels, label: Strings.Setup.Model(), required: true)
		.OnlyWhen("brand", "razer"),
	ActionParameter.Choice("logitechModel", logitechModels, label: Strings.Setup.Model(), required: true)
		.OnlyWhen("brand", "logitech")
]
```

Continue is gated on the fields that are shown: neither a closed advanced section nor a required field hidden
by `OnlyWhen` blocks it. A hidden field keeps its value and is still submitted, so read the field that
decides, not the presence of a value. Hosts up to 3.0.0-beta.4 ignore `OnlyWhen` here and show every field.

`ActionParameter.WidgetTarget` works here too, with the same picker actions use. A flow belongs to an
integration rather than a widget, so "This widget" (`$self`) is not offered and the field yields a
concrete widget id. Pass `WidgetTargetOptions` with `WidgetTypes` to limit the choice.

## Secrets

```csharp
ActionParameter.Secret("api_key", label: Strings.Setup.ApiKey(), required: true)

// Values that were never form fields:
return ConfigFlowResult.Complete(title, new Dictionary<string, ConfigFlowValue>
{
	["access_token"] = ConfigFlowValue.Secret(token.AccessToken),
	["refresh_token"] = ConfigFlowValue.Secret(token.RefreshToken),
	["display_name"] = ConfigFlowValue.Plain(profile.DisplayName)
});
```

- A `Secret` or `Password` field is stored encrypted by the host. You receive the plaintext in `input`.
- `Complete` can add values the user never saw, such as tokens from an OAuth exchange. `Secret` values are
  encrypted, `Plain` values stored as they are. A secret with an empty value is skipped.
- Secrets never belong in logs or in user-visible errors - log the failure type, not the value.
- A client submits a secret as plaintext. A submitted `{ "$secret": "<id>" }` is refused unless it is the
  reference the reconfigured entry already stores under the same key, so a flow can never be handed a
  secret that belongs to something else.

## Reading the entry at runtime

```csharp
public async Task InitializeAsync(IIntegrationContext context)
{
	foreach (var entry in await context.Config.GetEntriesAsync())
	{
		var serverUrl = await context.Config.GetStringAsync(entry.Id, "server_url");
		var apiKey = await context.Config.GetSecretAsync(entry.Id, "api_key");
		// Connect one client per entry. entry.Title is the name the user sees.
	}
}
```

`IIntegrationContext.Config` is an `IIntegrationConfig`. Secrets are only decrypted when you ask with
`GetSecretAsync`. `SetStringAsync` and `SetSecretAsync` write back to an entry - use `SetSecretAsync` for
rotating credentials such as a refreshed OAuth access token.

## OAuth

```csharp
// Step 1: collect the client id, then hand the user off to the provider.
var url = $"https://auth.example.com/authorize?client_id={Uri.EscapeDataString(clientId)}" +
	$"&redirect_uri={Uri.EscapeDataString(context.OAuth.RedirectUri)}" +
	$"&state={Uri.EscapeDataString(context.OAuth.State)}&response_type=code";
return ConfigFlowResult.External(url, resumeStepId: "authorize");

// Step 2: the host submits "authorize" once the callback arrives.
var code = context.OAuth.AuthorizationCode;
if (string.IsNullOrEmpty(code))
{
	return ConfigFlowResult.Error(WaitingStep(), Strings.Setup.AuthorizationNotCompleted());
}

var token = await ExchangeCodeAsync(clientId, clientSecret, code, context.OAuth.RedirectUri, cancellationToken);
return ConfigFlowResult.Complete("Example", new Dictionary<string, ConfigFlowValue>
{
	["access_token"] = ConfigFlowValue.Secret(token.AccessToken),
	["refresh_token"] = ConfigFlowValue.Secret(token.RefreshToken)
});
```

Macro Deck owns the redirect endpoint and matches the callback to your flow through `State`. You build the
provider's authorize URL and exchange the code; `AuthorizationCode` is `null` until the callback has
arrived. Each `External` result accepts one code: the first callback that carries one sets
`AuthorizationCode`, and later callbacks with the same `State` are ignored until the flow returns `External`
again. A callback that only reports an error leaves it `null`. Show `RedirectUri` as a copy value when the user has to register it with the provider. Do not
start your own callback listener. The built-in Spotify integration is a complete example.

A flow that talks to the service while it runs, to test a connection or exchange a code, should send the
User-Agent the user configured: `context.Http.UserAgent`, the same value as `IIntegrationContext.Http`, also
before the integration was ever initialized. See
[Outgoing HTTP requests](/reference/plugin-hosting/#outgoing-http-requests).

## Reconfiguring an entry

```csharp
private static List<ActionParameter> Fields(IConfigFlowContext context)
{
	var fields = new List<ActionParameter>();
	if ((context as IConfigFlowEntryContext)?.EntryTitle is null)
	{
		// Creating a new entry: ask for a name. Editing: the entry already has one.
		fields.Add(ActionParameter.Text("name", label: Strings.Setup.ConfigurationName(), required: true));
	}

	fields.Add(ActionParameter.Text("host", label: Strings.Setup.Host(), required: true));
	fields.Add(ActionParameter.Secret("password", label: Strings.Setup.Password()));
	return fields;
}
```

When the user edits an existing entry, the same flow runs again, pre-filled with the stored values:

- **`IConfigFlowEntryContext.EntryTitle`** is the existing or host-requested entry name; `null` means the
  flow chooses a title for a new entry. The context is optional - always cast with `as`, and keep working
  with a plain `IConfigFlowContext` (older hosts and plugins omit it).
- **An existing entry keeps its title.** The title passed to `Complete` is used only for a new entry that
  has none.
- **A secret field left empty keeps its stored secret**, and a `ConfigFlowValue.Secret` for that field is
  ignored unless the user typed a new one.

## Optional settings

A flow can also be the settings page of an integration that already works without it: a few switches
that are off by default, an interval with a sensible default. Return `false` from
`RequiresConfiguration`, and `false` from `AllowsMultipleConfigurations` so there is only ever one
settings entry:

```csharp
public sealed class SystemMediaIntegration : IPluginIntegration, IConfigFlowProvider
{
	public bool RequiresConfiguration => false;

	public bool AllowsMultipleConfigurations => false;

	public IConfigFlow CreateConfigFlow() => new SystemMediaSettingsFlow();

	public async Task InitializeAsync(IIntegrationContext context)
	{
		var entry = (await context.Config.GetEntriesAsync()).FirstOrDefault();
		var cycleApps = entry is not null &&
			await context.Config.GetStringAsync(entry.Id, "cycle_apps") is "true";
		// No entry yet: run with the defaults.
	}

	// Other IPluginIntegration members omitted.
}
```

With `RequiresConfiguration` set to `false`:

- **The integration runs with no config entry.** It starts enabled like any integration without a flow,
  unless its metadata opts out of that. `GetEntriesAsync` returns an empty list until the user saves the
  flow, so read your defaults then. Nothing creates an entry for you.
- **It is never shown as needing setup.** Turning it on just turns it on, its actions and variables are
  available right away, and the flow stays on the integration page to change the settings.
- **Saving the flow enables and reinitializes the integration**, as it does for every flow, so
  `InitializeAsync` reads the new values. That also turns it back on if the user had turned it off.
- **Removing the entry keeps the integration running** and reinitializes it, so it falls back to the
  defaults. A required flow instead turns the integration off when its last entry goes.
- **Several entries are possible if you allow them.** With `AllowsMultipleConfigurations` left at `true`
  the user can add more than one, and `GetEntriesAsync` returns all of them; for settings that is rarely
  what you want.

`RequiresConfiguration` is read when the integration is discovered, so keep it side-effect free. Hosts up
to 3.0.0-beta.14 ignore it and treat every flow as required, so on those the integration still waits for
the flow to be completed once.

## Completing without an entry

Some integrations create their entries themselves, for example one per device as each device connects, so
the dialog has nothing to store. Finish such a flow with `CompleteWithoutEntry()`:

```csharp
public Task<ConfigFlowResult> SubmitAsync(string stepId,
	IReadOnlyDictionary<string, object?> input,
	IConfigFlowContext context,
	CancellationToken cancellationToken)
	=> Task.FromResult(ConfigFlowResult.CompleteWithoutEntry());
```

The result's `Kind` is `Complete` and `StoresEntry` is `false`. Macro Deck closes the dialog, discards the
collected values, creates or changes no entry and does not initialize the integration again. A flow that
edits an existing entry leaves that entry as it is. If the user switched the integration off and it has no
usable entry, completing the flow removes that stored choice: going through setup counts as wanting the
integration again. An integration with a usable entry the user switched off stays off.

A Macro Deck that predates `CompleteWithoutEntry` treats it like `Complete` without a title: it stores an
entry from the collected values under the integration's name, enables the integration and initializes it
again. If such an entry would break your integration, refuse it in `PrepareEntryAsync` or report it as not
usable with `IsEntryUsableAsync`.

## Taking part in storing an entry

A flow decides what an entry holds. To also decide whether it may be stored, report how each stored entry
is doing, or apply a change without being initialized again, implement `IConfigEntryLifecycle` on the
integration. Every member has a default, so override only what you need:

```csharp
public sealed class ServersIntegration : IPluginIntegration, IConfigFlowProvider, IConfigEntryLifecycle
{
	public Task<ConfigEntryPreparationResult> PrepareEntryAsync(ConfigEntryPreparation preparation,
		CancellationToken cancellationToken = default)
	{
		var key = KeyFor(preparation.Title, preparation.Siblings);
		if (key is null)
		{
			return Task.FromResult(ConfigEntryPreparationResult.Refuse(Strings.Setup.NameTaken()));
		}

		var values = new Dictionary<string, JsonElement>(preparation.Values)
		{
			["key"] = JsonSerializer.SerializeToElement(key)
		};
		return Task.FromResult(ConfigEntryPreparationResult.Accept(values,
			[VariableDefinition.Eager($"server_{key}_status", VariableType.Text)]));
	}

	public Task<ConfigEntryStatus> GetEntryStatusAsync(StoredConfigEntry entry,
		CancellationToken cancellationToken = default)
		=> Task.FromResult(_connections.TryGetValue(entry.Id, out var connection) && connection.IsOpen
			? ConfigEntryStatus.Connected
			: ConfigEntryStatus.Disconnected);

	public async Task<bool> ReloadEntriesAsync(CancellationToken cancellationToken = default)
	{
		await ReconnectChangedEntriesAsync(cancellationToken);
		return true;
	}

	// Other members omitted.
}
```

Macro Deck handles one change at a time per integration, in this order:

1. **`PrepareEntryAsync`** sees the entry about to be stored: its id, title and values, the stored entry when
   it is an edit, the other entries, and whether the title changed. Return the values to store, or refuse
   with a message the user sees. `ReservedVariables` names the global variables the entry will create:
   Macro Deck holds those names until the change ends and refuses the entry with its own message when
   another integration's variable or another entry's preparation already has one. A variable your
   integration already owns under the same id and type is not a conflict, so saving an entry again works.
2. The entry is stored.
3. **`ReloadEntriesAsync`** applies the change in the running integration. Return `true` when you did;
   `false`, the default, makes Macro Deck shut the integration down and initialize it again.
4. **`SynchronizeEntriesAsync`** receives every stored entry, for example to create the variables of a new
   entry and delete those of a removed one.
5. **`ReleasePreparationAsync`** ends the preparation, whether or not the entry was stored.

### Synchronizing while the integration is off

Entries change while an integration is off too: the user can delete one, and an entry can be created before
the integration was ever initialized. Override the overload that takes the integration's own `IVariableApi`
and use it instead of `context.Variables`; it works whether or not the integration is initialized:

```csharp
public async Task SynchronizeEntriesAsync(IReadOnlyList<StoredConfigEntry> entries,
	IVariableApi variables,
	CancellationToken cancellationToken)
{
	var keys = entries.Select(KeyOf).OfType<string>().ToHashSet();
	foreach (var variable in await variables.GetAllAsync())
	{
		if (EntryKeyOf(variable.DefinitionId) is { } key && !keys.Contains(key))
		{
			await variables.DeleteAsync(variable.Id);
		}
	}

	foreach (var key in keys)
	{
		await variables.CreateAsync(VariableDefinition.Eager($"server_{key}_status", VariableType.Text));
	}
}
```

Macro Deck calls this overload; its default calls the overload without `variables`, so an integration written
before it keeps working unchanged. A Macro Deck that predates the overload calls the one without `variables`.
Out of process, `MacroDeck.Plugin.Hosting` passes the plugin's own variable api.

### Choosing another name when one is taken

A name in `ReservedVariables` can be taken by a variable you cannot see from `Siblings`, such as a user
variable. By default Macro Deck then refuses the entry with its own message. To pick another name instead,
set `RetryOnConflict` on the result. Macro Deck then prepares the entry again, with every name it could not
hold so far in `RejectedVariableNames`:

```csharp
var rejected = preparation.RejectedVariableNames.ToHashSet();
var key = Candidates(preparation.Title)
	.FirstOrDefault(candidate => !rejected.Contains($"server_{candidate}_status"));
if (key is null)
{
	return Task.FromResult(ConfigEntryPreparationResult.Refuse(Strings.Setup.NoFreeName()));
}

return Task.FromResult(ConfigEntryPreparationResult.Accept(values,
	[VariableDefinition.Eager($"server_{key}_status", VariableType.Text)]) with { RetryOnConflict = true });
```

`RejectedVariableNames` is empty on the first attempt and only grows within the same change, so the
integration keeps no state of its own. Macro Deck stops after 32 attempts, or when an attempt adds no new
rejected name, and refuses the entry with its own message. Refuse it yourself first when you run out of
candidates, so the user sees your message. A Macro Deck that predates `RetryOnConflict` ignores it and
refuses the entry on the first conflict.

`GetEntryStatusAsync` is what the integration page shows next to each entry, and `IsEntryUsableAsync` says
whether an entry can be used as it is. An integration whose flow is required is turned off when none of its
entries is usable, and renaming an unusable entry stores the new title without running `PrepareEntryAsync`.
A secret field of a `StoredConfigEntry` holds the reference to the encrypted value, not the value; read it
with `GetSecretAsync`.

A plugin that implements the interface declares the `config-entries` capability. A Macro Deck that predates
it rejects the declaration and never calls these members, and a plugin that cannot be reached while an entry
is stored gets the defaults: the entry is stored as the flow completed it and the integration is
initialized again.

## Rendering the flow as a UI tree

Implement `IUiConfigFlowProvider` on the integration and `IUiConfigFlow` on the flow to draw the flow as a
Macro Deck UI tree; `describe` then reports `servesConfigUiTree`. The declared steps are still served
either way, `SubmitAsync` is still the only way values are accepted, and the tree cannot tell the host
which of its values are secret - return those as `ConfigFlowValue.Secret` on completion. See
[Serving a configuration view](/ui/views/configuration/).

## Over the plugin protocol

A config flow is one `config-flow` capability, driven by `flow.start`, `flow.submit` and `flow.abandon`.
Its `describe` result carries `allowsMultipleConfigurations`, `servesConfigUiTree` and
`requiresConfiguration`; a plugin that sends no `requiresConfiguration` has a required flow.
`MacroDeck.Plugin.Hosting` keeps one `IConfigFlow` per host-minted session id, resolves secrets to
plaintext before `flow.submit`, and forwards OAuth state and `EntryTitle`. `PluginTestHarness.ConfigFlow`
drives the same operations in tests - see [Testing](/features/testing/) and
[the WebSocket reference](/reference/websocket/#capabilities).

`IConfigEntryLifecycle` is the separate `config-entries` capability, driven by `prepare`, `status`,
`usable`, `reload`, `synchronize` and `release`. The first integration of the plugin that implements it
answers them. `prepare` carries the optional `rejectedVariableNames` and its result the optional
`retryOnConflict`; a `Complete` result of `flow.submit` carries `withoutEntry: true` for
`CompleteWithoutEntry()`. All three are left out when unused. `PluginTestHarness.ConfigEntries` drives the
same operations in tests.

## See also

- [Plugin hosting](/reference/plugin-hosting/)
- [Authentication](/reference/authentication/)
- [Serving a configuration view](/ui/views/configuration/)
- [Localization](/features/localization/)
- [Integration issues](/features/integration-issues/)
