using System.Globalization;
using System.Text.Json;
using MacroDeck.Sdk.Migration;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.SoundPad;

internal sealed class SoundPadMacroDeck2Migration : IIntegrationMigration
{
	private const string ActionNamespace = "PW.MacroDeck.SoundPad.Actions.";

	public MigrationSource Source => MigrationSource.MacroDeck2;

	// The plugin's AssemblyName in every released version (0.1.0 to 2.1.2).
	public IReadOnlyList<string> ClaimedActionSources { get; } = ["SoundPadPlugin"];

	public IReadOnlyList<string> ClaimedSettingsSources { get; } = [];

	public Task<ActionMigrationResult?> MigrateActionAsync(ForeignAction action, CancellationToken cancellationToken)
		=> Task.FromResult(Migrate(action));

	public Task<IReadOnlyList<MigratedConfiguration>> MigrateConfigurationAsync(
		ForeignPluginSettings settings,
		CancellationToken cancellationToken)
		=> Task.FromResult<IReadOnlyList<MigratedConfiguration>>([]);

	private static ActionMigrationResult? Migrate(ForeignAction action) => action.TypeName switch
	{
		ActionNamespace + "PlayAction" => MigratePlay(action),
		ActionNamespace + "StopPlaybackAction" => Result(action, SoundPadActions.StopPlaybackId, []),
		ActionNamespace + "StartRecordingAction" => MigrateStartRecording(action),
		ActionNamespace + "StopRecordingAction" => Result(action, SoundPadActions.StopRecordingId, []),
		_ => null
	};

	private static ActionMigrationResult? MigratePlay(ForeignAction action)
	{
		if (!TryParseConfig(action.Configuration, out var config))
		{
			return null;
		}

		var index = TryGetProperty(config, "Sound", out var sound) && sound.ValueKind == JsonValueKind.Object
			? ReadInt(sound, "Index")
			: null;
		index ??= ReadInt(config, "AudioIndex");

		if (index is not > 0)
		{
			return null;
		}

		return Result(action,
			SoundPadActions.PlaySoundId,
			[(SoundPadActions.SoundParameter, index.Value.ToString(CultureInfo.InvariantCulture))]);
	}

	private static ActionMigrationResult? MigrateStartRecording(ForeignAction action)
	{
		if (!TryParseConfig(action.Configuration, out var config))
		{
			return null;
		}

		// An empty configuration deserialized to the enum default, Microphone, in Macro Deck 2.
		string? source = TryGetProperty(config, "RecordingDevice", out var device)
			? device.ValueKind switch
			{
				JsonValueKind.Number when device.TryGetInt32(out var value) => FromEnumValue(value),
				JsonValueKind.String => FromEnumName(device.GetString()),
				_ => null
			}
			: SoundPadActions.SourceMicrophone;

		return source is null
			? null
			: Result(action, SoundPadActions.StartRecordingId, [(SoundPadActions.SourceParameter, source)]);
	}

	private static string? FromEnumValue(int value) => value switch
	{
		0 => SoundPadActions.SourceMicrophone,
		1 => SoundPadActions.SourceSpeakers,
		_ => SoundPadActions.SourceDefault
	};

	private static string? FromEnumName(string? name)
	{
		if (string.Equals(name, "Microphone", StringComparison.OrdinalIgnoreCase))
		{
			return SoundPadActions.SourceMicrophone;
		}

		if (string.Equals(name, "Speakers", StringComparison.OrdinalIgnoreCase))
		{
			return SoundPadActions.SourceSpeakers;
		}

		return int.TryParse(name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
			? FromEnumValue(value)
			: null;
	}

	private static ActionMigrationResult Result(ForeignAction action, string actionId,
		IReadOnlyList<(string Name, string Value)> parameters)
		=> new(SoundPadIntegration.IntegrationId,
			actionId,
			action.DisplayName ?? actionId,
			parameters.ToDictionary(parameter => parameter.Name,
				parameter => JsonSerializer.SerializeToElement(parameter.Value),
				StringComparer.Ordinal),
			[AppStrings.Integrations.SoundPad.Migration.SetUpIntegration()]);

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

	private static int? ReadInt(JsonElement config, string name)
	{
		if (!TryGetProperty(config, name, out var value))
		{
			return null;
		}

		if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
		{
			return number;
		}

		return value.ValueKind == JsonValueKind.String &&
			int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
				? parsed
				: null;
	}
}
