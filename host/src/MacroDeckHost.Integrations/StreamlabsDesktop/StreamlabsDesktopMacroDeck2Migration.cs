using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.Migration;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.StreamlabsDesktop;

/// <summary>
/// Translates actions from Macro Deck 2's "Streamlabs OBS Plugin" (RecklessBoon.MacroDeck.StreamlabsOBSPlugin),
/// which targeted the old Streamlabs OBS desktop app over a local named pipe with no host, port or token of
/// its own.
/// </summary>
internal sealed class StreamlabsDesktopMacroDeck2Migration : IIntegrationMigration
{
	public MigrationSource Source => MigrationSource.MacroDeck2;

	private const string ActionNamespace = "RecklessBoon.MacroDeck.Streamlabs_OBS_Plugin.Actions.";

	public IReadOnlyList<string> ClaimedActionSources { get; } = ["Streamlabs OBS Plugin"];

	public IReadOnlyList<string> ClaimedSettingsSources { get; } = ["recklessboon_streamlabs obs plugin"];

	private static LocalizedText SceneOrSourceNameWarning
		=> AppStrings.Migration.Warning.StreamlabsDesktop.SceneSourceMatchedByName();

	public Task<ActionMigrationResult?> MigrateActionAsync(ForeignAction action, CancellationToken cancellationToken)
		=> Task.FromResult(Migrate(action));

	public Task<IReadOnlyList<MigratedConfiguration>> MigrateConfigurationAsync(
		ForeignPluginSettings settings,
		CancellationToken cancellationToken)
		=> Task.FromResult(MigrateConfiguration(settings));

	private static ActionMigrationResult? Migrate(ForeignAction action) => action.TypeName switch
	{
		ActionNamespace + "SwitchSceneAction" => SwitchScene(action),
		ActionNamespace + "SetAudioSourceMuteAction" => SetAudioMute(action),
		ActionNamespace + "SetAudioSourceVolumeAction" => SetAudioVolume(action),
		ActionNamespace + "SetRecordingStateAction"
			=> SetState(action, "start-recording", "stop-recording", "toggle-recording", "Set Recording State"),
		ActionNamespace + "SetStreamingStateAction"
			=> SetState(action, "start-streaming", "stop-streaming", "toggle-streaming", "Set Streaming State"),
		ActionNamespace + "SetReplayBufferStateAction"
			=> SetState(action,
				"start-replay-buffer",
				"stop-replay-buffer",
				"toggle-replay-buffer",
				"Set Replay Buffer State"),
		ActionNamespace + "SaveReplayAction" => new ActionMigrationResult(StreamlabsDesktopIntegration.IntegrationId,
			"save-replay",
			action.DisplayName ?? "Save Replay",
			Parameters()),
		ActionNamespace + "SetSceneItemSettingsAction" => SetSceneItemVisibility(action),
		_ => null
	};

	/// <summary>
	/// Nothing is worth carrying over: the plugin's only stored value is a "client_secret" credential that
	/// <c>Client.cs</c> never reads (the pipe connection it makes is unauthenticated), and the plugin
	/// declares <c>CanConfigure =&gt; false</c>, so no shipped build could even open the dialog that wrote it.
	/// </summary>
	private static IReadOnlyList<MigratedConfiguration> MigrateConfiguration(ForeignPluginSettings settings) => [];

	/// <summary>
	/// The scene id Macro Deck 2 stored is local to Streamlabs OBS and cannot resolve in Streamlabs Desktop,
	/// so the scene name the configurator wrote into the summary ("Switch to [Collection] Scene") is used
	/// instead.
	/// </summary>
	private static ActionMigrationResult? SwitchScene(ForeignAction action)
	{
		var sceneName = NameAfterBracket(action.ConfigurationSummary, "Switch to ");
		if (sceneName is null)
		{
			return null;
		}

		return new ActionMigrationResult(StreamlabsDesktopIntegration.IntegrationId,
			"set-scene",
			action.DisplayName ?? "Switch Scene",
			Parameters(("scene", JsonString(sceneName))),
			[SceneOrSourceNameWarning]);
	}

	private static ActionMigrationResult? SetAudioMute(ForeignAction action)
	{
		var summary = action.ConfigurationSummary;
		var (mode, prefix) = summary switch
		{
			not null when summary.StartsWith("Toggle ", StringComparison.Ordinal) => ("toggle", "Toggle "),
			not null when summary.StartsWith("Unmute ", StringComparison.Ordinal) => ("unmute", "Unmute "),
			not null when summary.StartsWith("Mute ", StringComparison.Ordinal) => ("mute", "Mute "),
			_ => (null, null)
		};

		if (mode is null || summary![prefix!.Length..] is not { Length: > 0 } sourceName)
		{
			return null;
		}

		return new ActionMigrationResult(StreamlabsDesktopIntegration.IntegrationId,
			"set-audio-mute",
			action.DisplayName ?? "Mute/Unmute Audio Source",
			Parameters(("source", JsonString(sourceName)), ("mode", JsonString(mode))),
			[SceneOrSourceNameWarning]);
	}

	/// <summary>
	/// The plugin only ever sets an absolute deflection, never a relative one, so this always maps to
	/// Streamlabs Desktop's "set" mode rather than its increase/decrease modes.
	/// </summary>
	private static ActionMigrationResult? SetAudioVolume(ForeignAction action)
	{
		if (!TryParse(action.Configuration, out var config) || ReadDouble(config, "Deflection") is not { } deflection)
		{
			return null;
		}

		var percent = Math.Clamp((int)Math.Round(deflection * 100), 0, 100);

		const string prefix = "Set ";
		var suffix = $" to {percent}%";
		var summary = action.ConfigurationSummary;
		if (summary is null ||
			summary.Length < prefix.Length + suffix.Length ||
			!summary.StartsWith(prefix, StringComparison.Ordinal) ||
			!summary.EndsWith(suffix, StringComparison.Ordinal) ||
			summary[prefix.Length..^suffix.Length] is not { Length: > 0 } sourceName)
		{
			return null;
		}

		return new ActionMigrationResult(StreamlabsDesktopIntegration.IntegrationId,
			"set-audio-volume",
			action.DisplayName ?? "Set Audio Source Volume/Deflection",
			Parameters(("source", JsonString(sourceName)),
				("mode", JsonString("set")),
				("volume", JsonNumber(percent))),
			[SceneOrSourceNameWarning]);
	}

	private static ActionMigrationResult? SetState(
		ForeignAction action,
		string startId,
		string stopId,
		string toggleId,
		string fallbackName)
	{
		if (!TryParse(action.Configuration, out var config))
		{
			return null;
		}

		var id = ReadInt(config, "ActionType") switch
		{
			1 => startId,
			2 => stopId,
			3 => toggleId,
			_ => null
		};

		return id is null
			? null
			: new ActionMigrationResult(StreamlabsDesktopIntegration.IntegrationId,
				id,
				action.DisplayName ?? fallbackName,
				Parameters());
	}

	/// <summary>
	/// Only the plain visibility flag has a Streamlabs Desktop equivalent. The lock state and the separate
	/// recording/stream output visibility flags do not, so a button using those is reported as dropped
	/// rather than silently ignored.
	/// </summary>
	private static ActionMigrationResult? SetSceneItemVisibility(ForeignAction action)
	{
		if (!TryParse(action.Configuration, out var config) ||
			!config.TryGetProperty("Settings", out var settings) ||
			settings.ValueKind != JsonValueKind.Object)
		{
			return null;
		}

		var mode = ReadInt(settings, "Visible") switch
		{
			1 => "show",
			2 => "hide",
			3 => "toggle",
			_ => null
		};

		if (mode is null)
		{
			return null;
		}

		var (itemName, sceneName) = NamesFromSceneItemSummary(action.ConfigurationSummary);
		if (itemName is null || sceneName is null)
		{
			return null;
		}

		var warnings = new List<LocalizedText> { SceneOrSourceNameWarning };
		if (ReadInt(settings, "Locked") is > 0 ||
			ReadInt(settings, "RecordingVisible") is > 0 ||
			ReadInt(settings, "StreamVisible") is > 0)
		{
			warnings.Add(AppStrings.Migration.Warning.StreamlabsDesktop.SceneItemExtrasNotMigrated());
		}

		return new ActionMigrationResult(StreamlabsDesktopIntegration.IntegrationId,
			"set-source-visibility",
			action.DisplayName ?? "Update Scene Item Settings",
			Parameters(("scene", JsonString(sceneName)), ("source", JsonString(itemName)), ("mode", JsonString(mode))),
			warnings);
	}

	private static string? NameAfterBracket(string? summary, string prefix)
	{
		if (summary is null || !summary.StartsWith(prefix, StringComparison.Ordinal))
		{
			return null;
		}

		var closing = summary.IndexOf("] ", prefix.Length, StringComparison.Ordinal);
		if (closing < 0)
		{
			return null;
		}

		var name = summary[(closing + 2)..];
		return name.Length > 0 ? name : null;
	}

	private static (string? Item, string? Scene) NamesFromSceneItemSummary(string? summary)
	{
		const string prefix = "Update ";
		const string middle = " in scene [";
		if (summary is null || !summary.StartsWith(prefix, StringComparison.Ordinal))
		{
			return (null, null);
		}

		var middleIndex = summary.IndexOf(middle, prefix.Length, StringComparison.Ordinal);
		if (middleIndex < 0)
		{
			return (null, null);
		}

		var closing = summary.IndexOf("] ", middleIndex + middle.Length, StringComparison.Ordinal);
		if (closing < 0)
		{
			return (null, null);
		}

		var item = summary[prefix.Length..middleIndex];
		var scene = summary[(closing + 2)..];
		return item.Length > 0 && scene.Length > 0 ? (item, scene) : (null, null);
	}

	private static bool TryParse(string? configuration, out JsonElement config)
	{
		config = default;
		if (string.IsNullOrWhiteSpace(configuration))
		{
			return false;
		}

		try
		{
			config = JsonDocument.Parse(configuration).RootElement.Clone();
			return config.ValueKind == JsonValueKind.Object;
		}
		catch (JsonException)
		{
			return false;
		}
	}

	private static int? ReadInt(JsonElement config, string name)
		=> config.TryGetProperty(name, out var value) &&
			value.ValueKind == JsonValueKind.Number &&
			value.TryGetInt32(out var number)
				? number
				: null;

	private static double? ReadDouble(JsonElement config, string name)
		=> config.TryGetProperty(name, out var value) &&
			value.ValueKind == JsonValueKind.Number &&
			value.TryGetDouble(out var number)
				? number
				: null;

	private static JsonElement JsonString(string value) => JsonSerializer.SerializeToElement(value);

	private static JsonElement JsonNumber(int value) => JsonSerializer.SerializeToElement(value);

	private static Dictionary<string, JsonElement> Parameters(params (string Name, JsonElement Value)[] values)
		=> values.ToDictionary(entry => entry.Name, entry => entry.Value, StringComparer.Ordinal);
}
