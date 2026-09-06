using System.Text.Json;
using MacroDeck.Sdk.Migration;
using MacroDeckHost.Integrations.HomeAssistant.Actions;

namespace MacroDeckHost.Integrations.HomeAssistant;

/// <summary>
/// Translates Macro Deck 2's Home Assistant plugin into <see cref="HomeAssistantIntegration" />'s
/// call-service action and configuration entries. Kept out of <see cref="HomeAssistantIntegration" />
/// itself so that class stays readable; this is only ever called through its <see cref="IIntegrationMigration" />
/// members.
/// </summary>
internal sealed class HomeAssistantMacroDeck2Migration : IIntegrationMigration
{
	public MigrationSource Source => MigrationSource.MacroDeck2;

	// Home Assistant Plugin.csproj's file name (the project has no explicit AssemblyName, so MSBuild
	// derives it from the project file), also the "dll" field of its ExtensionManifest.json minus the
	// extension.
	public IReadOnlyList<string> ClaimedActionSources { get; } = ["Home Assistant Plugin"];

	// Macro Deck 2 derives its settings/credentials file name from the plugin's author ("Macro Deck") and
	// display name ("Home Assistant Plugin"), lowercased and joined with an underscore.
	public IReadOnlyList<string> ClaimedSettingsSources { get; } = ["macro deck_home assistant plugin"];

	public Task<ActionMigrationResult?> MigrateActionAsync(ForeignAction action, CancellationToken cancellationToken)
		=> Task.FromResult(Migrate(action));

	public Task<IReadOnlyList<MigratedConfiguration>> MigrateConfigurationAsync(
		ForeignPluginSettings settings,
		CancellationToken cancellationToken)
		=> Task.FromResult(MigrateConfiguration(settings));

	private static ActionMigrationResult? Migrate(ForeignAction action)
	{
		if (action.TypeName != "SuchByte.HomeAssistantPlugin.Actions.CallServiceAction")
		{
			return null;
		}

		if (!TryParseConfig(action.Configuration, out var config))
		{
			return null;
		}

		var service = ReadString(config, "service");
		var separator = service?.IndexOf('.') ?? -1;
		if (separator <= 0 || separator == service!.Length - 1)
		{
			return null;
		}

		var domain = service[..separator];
		var serviceName = service[(separator + 1)..];

		var parameters = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
		{
			[CallServiceActionDefinition.DomainParameterName] = JsonSerializer.SerializeToElement(domain),
			[CallServiceActionDefinition.ServiceParameterName] = JsonSerializer.SerializeToElement(serviceName)
		};

		if (ReadString(config, "entityId") is { Length: > 0 } entityId)
		{
			parameters[CallServiceActionDefinition.EntitiesParameterName] =
				JsonSerializer.SerializeToElement(new[] { entityId });
		}

		return new ActionMigrationResult(HomeAssistantIntegration.IntegrationId,
			"call-service",
			action.DisplayName ?? service,
			parameters);
	}

	private static IReadOnlyList<MigratedConfiguration> MigrateConfiguration(ForeignPluginSettings settings)
	{
		var host = ReadCredential(settings.Credentials, "host");
		var token = ReadCredential(settings.Credentials, "token");
		if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(token))
		{
			return [];
		}

		var ssl = bool.TryParse(ReadCredential(settings.Credentials, "ssl"), out var parsedSsl) && parsedSsl;

		// Macro Deck 2 stored the host and the "use SSL" flag separately (the connect screen only asked
		// for "<ip address/hostname>:8123"); Macro Deck 3's HomeAssistantEndpoint expects one baseUrl with
		// the scheme already in it, so the two are combined here.
		var baseUrl = host.Contains("://", StringComparison.Ordinal) ? host : $"{(ssl ? "https" : "http")}://{host}";

		var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
		{
			[HomeAssistantConfigKeys.BaseUrl] = JsonSerializer.SerializeToElement(baseUrl)
		};

		var secrets = new Dictionary<string, MigratedSecret>(StringComparer.Ordinal)
		{
			[HomeAssistantConfigKeys.Token] = new(token, MigratedSecretKind.Secret)
		};

		return
		[
			new MigratedConfiguration(HomeAssistantIntegration.IntegrationId,
				"Home Assistant",
				values,
				secrets)
		];
	}

	private static string? ReadCredential(IReadOnlyList<IReadOnlyDictionary<string, string>> credentials,
		string name)
		=> credentials
			.SelectMany(dict => dict)
			.FirstOrDefault(pair => string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
			.Value;

	private static bool TryParseConfig(string? configuration, out JsonElement config)
	{
		var text = string.IsNullOrWhiteSpace(configuration) ? "{}" : configuration;
		try
		{
			config = JsonDocument.Parse(text).RootElement.Clone();
			return config.ValueKind == JsonValueKind.Object;
		}
		catch (JsonException)
		{
			config = default;
			return false;
		}
	}

	private static bool TryGetProperty(JsonElement config, string name, out JsonElement value)
	{
		foreach (var property in config.EnumerateObject())
		{
			if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
			{
				value = property.Value;
				return true;
			}
		}

		value = default;
		return false;
	}

	private static string? ReadString(JsonElement config, string name)
		=> TryGetProperty(config, name, out var value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;
}
