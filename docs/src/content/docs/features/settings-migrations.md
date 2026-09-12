---
title: Settings migrations
description: Take a plugin's buttons, connections and credentials over from another application such as Macro Deck 2 with IMigrationProvider and IIntegrationMigration.
---

An integration implements `IMigrationProvider` so someone arriving from Macro Deck 2 keeps the buttons and
connections they already had. This page is about another application's setup; for upgrading your plugin
to a newer SDK or protocol, see [SDK and protocol migrations](/policies/migrations/).

## Quick start

```csharp
using System.Text.Json;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Migration;

public sealed class ObsIntegration : IPluginIntegration, IMigrationProvider
{
	public IReadOnlyList<IIntegrationMigration> Migrations { get; } = [new ObsMacroDeck2Migration()];

	// IPluginIntegration members omitted.
}

internal sealed class ObsMacroDeck2Migration : IIntegrationMigration
{
	private const string IntegrationId = "com.example.obs"; // the id in your manifest.json

	public MigrationSource Source => MigrationSource.MacroDeck2;

	// Macro Deck 2 names the assembly an action type lives in.
	public IReadOnlyList<string> ClaimedActionSources { get; } = ["OBS-WebSocket Plugin"];

	// Macro Deck 2's settings file name: "<author>_<plugin name>", lowercased.
	public IReadOnlyList<string> ClaimedSettingsSources { get; } = ["macro deck_obs-websocket plugin"];

	public Task<ActionMigrationResult?> MigrateActionAsync(ForeignAction action, CancellationToken cancellationToken)
		=> Task.FromResult(action.TypeName switch
		{
			"SuchByte.OBSWebSocketPlugin.Actions.SetSceneAction" => MigrateSetScene(action),
			_ => null
		});

	public Task<IReadOnlyList<MigratedConfiguration>> MigrateConfigurationAsync(
		ForeignPluginSettings settings, CancellationToken cancellationToken)
		=> Task.FromResult<IReadOnlyList<MigratedConfiguration>>([]);

	private static ActionMigrationResult? MigrateSetScene(ForeignAction action)
	{
		var scene = ReadString(action.Configuration, "SceneName");
		if (string.IsNullOrWhiteSpace(scene))
		{
			return null;
		}

		return new ActionMigrationResult(IntegrationId, "set-scene", action.DisplayName ?? "Set OBS scene",
			new Dictionary<string, JsonElement> { ["scene"] = JsonSerializer.SerializeToElement(scene) });
	}

	private static string? ReadString(string? json, string name) { /* parse leniently, null on failure */ }
}
```

Every Macro Deck 2 "Set scene" button now arrives as your `set-scene` action with its scene filled in.
Every other OBS button arrives as a placeholder carrying its original configuration.

Things to know:

- **`Migrations` is a list, one entry per source application.** An integration commonly reads several
  (`MigrationSource.MacroDeck2`, `TouchPortal`, `Deckboard`); two entries never share a `Source`.
- **You claim, the host reads.** Finding the foreign installation, reading its files and decrypting its
  credentials is the host's work. It then asks whoever claims what it found.
- **Action and settings claims are separate lists.** `ClaimedActionSources` matches what the source uses
  to say which plugin an action came from; `ClaimedSettingsSources` matches its stored per-plugin
  settings and credentials. An application need not name a plugin the same way in both.

## Translating actions

```csharp
private static ActionMigrationResult? MigrateChatMode(ForeignAction action, string mode)
{
	var method = ReadEnumIndex(action.Configuration, "Method"); // 0 = on, 1 = off, 2 = toggle
	if (method is not (0 or 1))
	{
		return null; // "toggle" has no equivalent: this plugin's action only sets a fixed value
	}

	return new ActionMigrationResult(IntegrationId, "set-chat-mode", action.DisplayName ?? "Set chat mode",
		new Dictionary<string, JsonElement>
		{
			["mode"] = JsonSerializer.SerializeToElement(mode),
			["enabled"] = JsonSerializer.SerializeToElement(method == 0)
		});
}
```

`ForeignAction` is the action as the source stored it: `TypeName`, `ActionSource`, `DisplayName`, and an
opaque `Configuration` string you parse yourself. Switch on `TypeName`.

**`null` is a normal answer, not a failure.** Return it when there is no equivalent, or when the
configuration cannot be read - never throw, because one unreadable button must not end a migration. The
host keeps the action as a placeholder with its original configuration, which is better than an action
that quietly does something else.

`Parameters` carries only the values the source knew. Type, label and options come from your action's
own definition, so a migration never restates them.

## Migrating configuration and credentials

```csharp
public Task<IReadOnlyList<MigratedConfiguration>> MigrateConfigurationAsync(
	ForeignPluginSettings settings, CancellationToken cancellationToken)
{
	var results = new List<MigratedConfiguration>();

	foreach (var credentials in settings.Credentials)
	{
		if (!credentials.TryGetValue("host", out var host) || string.IsNullOrWhiteSpace(host))
		{
			continue; // no host: this entry would look configured and never connect
		}

		var title = credentials.TryGetValue("name", out var name) && name.Length > 0 ? name : "OBS Connection";
		var values = new Dictionary<string, JsonElement> { ["host"] = JsonSerializer.SerializeToElement(host) };

		var secrets = new Dictionary<string, MigratedSecret>();
		if (credentials.TryGetValue("password", out var password) && password.Length > 0)
		{
			secrets["password"] = new MigratedSecret(password, MigratedSecretKind.Secret);
		}

		results.Add(new MigratedConfiguration(IntegrationId, title, values, secrets));
	}

	return Task.FromResult<IReadOnlyList<MigratedConfiguration>>(results);
}
```

`ForeignPluginSettings` holds the plugin's stored `Settings`, its already-decrypted `Credentials`, and
every one of its `Actions` the source found - because an application need not keep all of a plugin's
configuration in its settings file (Macro Deck 2 stored the SinusBot login there, but the bot instance
in each button).

**`Credentials` may be empty.** The user can decline to decrypt them, and an application that ties
credentials to the machine that wrote them cannot open a folder copied off another one. Return nothing
rather than an entry that looks configured but cannot work.

Put secret values in `Secrets`, never in `Values`: the host stores each in its secret store and leaves only
a reference in the entry. `MigratedSecretKind.Password` is a password the user chose and may be shown
again; `Secret` is a token or key they never typed.

## Reporting warnings

```csharp
return new ActionMigrationResult(IntegrationId, "set-scene", action.DisplayName ?? "Set OBS scene",
	parameters,
	Warnings: [Strings.Migration.ConnectionNotMigrated(name: connectionName)]);
```

Anything a translation could not carry across exactly goes in `Warnings`. A partial translation is useful
as long as it is honest about what it dropped. Warnings are `LocalizedText`, so a generated
[`Strings` member](/features/localization/#the-generated-api) reads in the language of whoever opens the
migration wizard.

## Edge cases

- **Any failure over the protocol counts as "no equivalent".** An unreachable plugin, a timeout or a
  malformed result costs one placeholder, not the migration. See
  [capability parity](/reference/capability-parity/).
- **Unknown sources are ignored.** A source name the host does not recognise is dropped from the
  declaration rather than refused, so a plugin built against a later SDK stays usable.
- **Both members are asynchronous** because an out-of-process plugin answers over its connection. Pure
  in-process work returns `Task.FromResult` and costs nothing.

## Over the plugin protocol

The `migration` capability kind, declared at the single local id `provider`; the source application
travels in each invocation's arguments.

| Operation | Purpose |
| --- | --- |
| `describe` | Every source this plugin migrates from, with its claimed action and settings sources. |
| `migrate-action` | Translate one foreign action. `translated: false` is the "no equivalent" answer. |
| `migrate-configuration` | Turn a foreign plugin's settings and credentials into configuration entries. Secrets travel separately from plain values. |

See [the WebSocket reference](/reference/websocket/#capabilities).

## See also

- [Actions](/features/actions/) - the action ids and parameters a translation targets
- [Setup flows](/features/setup-flows/) - the configuration entries a migration creates
- [Localization](/features/localization/) - generating `Strings` for warnings
- [SDK and protocol migrations](/policies/migrations/) - upgrading your plugin, not the user's setup
