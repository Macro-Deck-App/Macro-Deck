---
title: Setup flows
description: Guide users through connecting an integration with IConfigFlowProvider - steps, fields, validation errors, secrets, OAuth and reconfiguring an entry.
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

## Outcomes

Every `StartAsync` and `SubmitAsync` returns a `ConfigFlowResult`:

| Factory | What the host does |
| --- | --- |
| `Step(step)` | Shows `step`. |
| `Error(step, message, fieldErrors)` | Shows `step` again with a general message and/or per-field messages keyed by field name. Values from this submit are discarded. |
| `External(url, resumeStepId)` | Opens `url` in the browser, waits for the redirect callback, then submits `resumeStepId`. See [OAuth](#oauth). |
| `Complete(title, values)` | Saves the entry: all collected form values plus the extra `values`. |

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
OAuth client id, a non-default endpoint - not for normal setup. Continue is gated on the visible fields
only, and the section opens by itself when one of its fields already has a value.

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
arrived. Show `RedirectUri` as a copy value when the user has to register it with the provider. Do not
start your own callback listener. The built-in Spotify integration is a complete example.

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

## Rendering the flow as a UI tree

Implement `IUiConfigFlowProvider` on the integration and `IUiConfigFlow` on the flow to draw the flow as a
Macro Deck UI tree; `describe` then reports `servesConfigUiTree`. The declared steps are still served
either way, `SubmitAsync` is still the only way values are accepted, and the tree cannot tell the host
which of its values are secret - return those as `ConfigFlowValue.Secret` on completion. See
[Serving a configuration view](/ui/views/configuration/).

## Over the plugin protocol

A config flow is one `config-flow` capability, driven by `flow.start`, `flow.submit` and `flow.abandon`.
`MacroDeck.Plugin.Hosting` keeps one `IConfigFlow` per host-minted session id, resolves secrets to
plaintext before `flow.submit`, and forwards OAuth state and `EntryTitle`. `PluginTestHarness.ConfigFlow`
drives the same operations in tests - see [Testing](/features/testing/) and
[the WebSocket reference](/reference/websocket/#capabilities).

## See also

- [Plugin hosting](/reference/plugin-hosting/)
- [Authentication](/reference/authentication/)
- [Serving a configuration view](/ui/views/configuration/)
- [Localization](/features/localization/)
- [Integration issues](/features/integration-issues/)
