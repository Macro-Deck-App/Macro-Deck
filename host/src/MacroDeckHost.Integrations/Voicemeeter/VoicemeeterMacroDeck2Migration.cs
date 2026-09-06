using System.Text.Json;
using System.Text.RegularExpressions;
using MacroDeck.Sdk.Migration;
using MacroDeckHost.Integrations.Voicemeeter.Actions;

namespace MacroDeckHost.Integrations.Voicemeeter;

/// <summary>
/// Translates actions from PhoenixWyllow's Macro Deck 2 Voicemeeter plugin. That plugin's device actions
/// store a foreign channel id such as <c>Strip(0)</c> - a plugin-internal spelling, not the native
/// <c>Strip[0]</c> the Remote API and this integration both use - so every parameter is rebuilt from its
/// parsed parts rather than carried across as text.
/// </summary>
internal sealed class VoicemeeterMacroDeck2Migration : IIntegrationMigration
{
	public MigrationSource Source => MigrationSource.MacroDeck2;

	public IReadOnlyList<string> ClaimedActionSources { get; } = ["Voicemeeter Plugin"];

	public IReadOnlyList<string> ClaimedSettingsSources { get; } = ["phoenixwyllow_voicemeeter plugin"];

	private static readonly Regex _channelId = new(@"^(Strip|Bus)\((\d+)\)$", RegexOptions.Compiled);

	private static readonly Regex _busAssignment = new(@"^[AB][0-9]+$", RegexOptions.Compiled);

	public Task<ActionMigrationResult?> MigrateActionAsync(ForeignAction action, CancellationToken cancellationToken)
		=> Task.FromResult(Migrate(action));

	public Task<IReadOnlyList<MigratedConfiguration>> MigrateConfigurationAsync(
		ForeignPluginSettings settings,
		CancellationToken cancellationToken)
		=> Task.FromResult(MigrateConfiguration(settings));

	private static ActionMigrationResult? Migrate(ForeignAction action) => action.TypeName switch
	{
		"PW.VoicemeeterPlugin.Actions.AdvancedAction" => MigrateAdvanced(action),
		"PW.VoicemeeterPlugin.Actions.CommandAction" => MigrateCommand(action),
		"PW.VoicemeeterPlugin.Actions.DeviceSliderAction" => MigrateSlider(action),
		"PW.VoicemeeterPlugin.Actions.DeviceToggleAction" => MigrateToggle(action),
		"PW.VoicemeeterPlugin.Actions.MacroButtonAction" => MigrateMacroButton(action),
		_ => null
	};

	// Macro Deck 3 talks to Voicemeeter directly through its Remote API rather than through a configured
	// host/port/credential set, so there is nothing in the foreign plugin's settings worth carrying over.
	private static IReadOnlyList<MigratedConfiguration> MigrateConfiguration(ForeignPluginSettings settings) => [];

	// AdvancedAction stores a raw Remote API script - the exact grammar RunScriptActionDefinition runs -
	// rather than JSON, so it needs no parsing at all.
	private static ActionMigrationResult? MigrateAdvanced(ForeignAction action)
	{
		var script = action.Configuration?.Trim();
		return string.IsNullOrEmpty(script)
			? null
			: new ActionMigrationResult(VoicemeeterIntegration.IntegrationId,
				"run-script",
				action.DisplayName ?? "Run Voicemeeter script",
				Parameters((RunScriptActionDefinition.ScriptParameter, JsonString(script))));
	}

	private static ActionMigrationResult? MigrateCommand(ForeignAction action)
	{
		if (!TryParseObject(action.Configuration, out var config) ||
			!TryReadNestedInt(config, "Command", "CommandType", out var commandType))
		{
			return null;
		}

		var value = ReadString(config, "CommandValue");

		return commandType switch
		{
			0 => RunCommand(action, "shutdown", "Shut down Voicemeeter"),
			1 => RunCommand(action, "restart", "Restart Voicemeeter"),
			2 => RunCommand(action, "show", "Show Voicemeeter"),
			3 => RunCommand(action, "reset", "Reset Voicemeeter configuration"),
			5 when value is { Length: > 0 } file => new ActionMigrationResult(VoicemeeterIntegration.IntegrationId,
				"load-settings",
				action.DisplayName ?? "Load Voicemeeter settings",
				Parameters((LoadSettingsActionDefinition.FileParameter, JsonString(file)))),
			6 => RunCommand(action, "eject", "Eject Voicemeeter cassette"),
			// ConfigSave and RecorderLoad have no dedicated Macro Deck 3 action, but they are still just the
			// Remote API parameters "Command.Save" and "Recorder.Load" underneath, so set-parameter carries
			// them across exactly instead of dropping them.
			4 when value is { Length: > 0 } file => new ActionMigrationResult(VoicemeeterIntegration.IntegrationId,
				"set-parameter",
				action.DisplayName ?? "Save Voicemeeter settings",
				Parameters((SetParameterActionDefinition.ParameterNameParameter,
						JsonString(VoicemeeterParameters.Command("Save"))),
					(SetParameterActionDefinition.ValueParameter, JsonString(file)))),
			7 when value is { Length: > 0 } file => new ActionMigrationResult(VoicemeeterIntegration.IntegrationId,
				"set-parameter",
				action.DisplayName ?? "Load Voicemeeter recorder cassette",
				Parameters((SetParameterActionDefinition.ParameterNameParameter,
						JsonString(VoicemeeterParameters.Recorder("Load"))),
					(SetParameterActionDefinition.ValueParameter, JsonString(file)))),
			_ => null
		};
	}

	private static ActionMigrationResult RunCommand(ForeignAction action, string command, string fallbackLabel)
		=> new(VoicemeeterIntegration.IntegrationId,
			"run-command",
			action.DisplayName ?? fallbackLabel,
			Parameters(("command", JsonString(command))));

	// The slider nudges a channel's gain by a delta relative to its last known value rather than setting
	// it outright, which is exactly what set-{target}-gain's increase/decrease mode does.
	private static ActionMigrationResult? MigrateSlider(ForeignAction action)
	{
		if (!TryParseObject(action.Configuration, out var config) ||
			!TryReadOption(config, out var id, out var option) ||
			!string.Equals(option, VoicemeeterParameters.Gain, StringComparison.Ordinal) ||
			!TryParseChannel(id, out var kind, out var index) ||
			!TryReadDouble(config, "Value", out var delta))
		{
			return null;
		}

		var target = kind == VoicemeeterChannelKind.Strip
			? VoicemeeterChannelTarget.Strip
			: VoicemeeterChannelTarget.Bus;
		var mode = delta < 0 ? "decrease" : "increase";

		return new ActionMigrationResult(VoicemeeterIntegration.IntegrationId,
			$"set-{target.IdPrefix}-gain",
			action.DisplayName ?? $"Adjust {target.Noun} gain",
			Parameters((target.ParameterName, JsonNumber(index)),
				(VoicemeeterActionValues.ModeParameter, JsonString(mode)),
				(VoicemeeterActionValues.GainParameter, JsonNumber(Math.Abs(delta)))));
	}

	// The toggle flips a channel's current boolean state, matching set-{target}-mute / set-{target}-switch
	// / set-strip-routing's own "toggle" mode exactly. Only the built-in options this plugin ships are
	// recognised; a user-defined additional variable has no known Macro Deck 3 counterpart to toggle.
	private static ActionMigrationResult? MigrateToggle(ForeignAction action)
	{
		if (!TryParseObject(action.Configuration, out var config) ||
			!TryReadOption(config, out var id, out var option) ||
			!TryParseChannel(id, out var kind, out var index))
		{
			return null;
		}

		var target = kind == VoicemeeterChannelKind.Strip
			? VoicemeeterChannelTarget.Strip
			: VoicemeeterChannelTarget.Bus;

		if (string.Equals(option, VoicemeeterParameters.Mute, StringComparison.Ordinal))
		{
			return new ActionMigrationResult(VoicemeeterIntegration.IntegrationId,
				$"set-{target.IdPrefix}-mute",
				action.DisplayName ?? $"Toggle {target.Noun} mute",
				Parameters((target.ParameterName, JsonNumber(index)),
					(VoicemeeterActionValues.ModeParameter, JsonString("toggle"))));
		}

		if (IsSwitch(kind, option))
		{
			return new ActionMigrationResult(VoicemeeterIntegration.IntegrationId,
				$"set-{target.IdPrefix}-switch",
				action.DisplayName ?? $"Toggle {target.Noun} {option}",
				Parameters((target.ParameterName, JsonNumber(index)),
					(VoicemeeterActionValues.SwitchParameter, JsonString(option)),
					(VoicemeeterActionValues.ModeParameter, JsonString("toggle"))));
		}

		if (kind == VoicemeeterChannelKind.Strip && _busAssignment.IsMatch(option))
		{
			return new ActionMigrationResult(VoicemeeterIntegration.IntegrationId,
				"set-strip-routing",
				action.DisplayName ?? $"Toggle strip routing to {option}",
				Parameters((VoicemeeterActionValues.StripParameter, JsonNumber(index)),
					(VoicemeeterActionValues.BusParameter, JsonString(option)),
					(VoicemeeterActionValues.ModeParameter, JsonString("toggle"))));
		}

		return null;
	}

	private static bool IsSwitch(VoicemeeterChannelKind kind, string option) => kind switch
	{
		VoicemeeterChannelKind.Strip => option is VoicemeeterParameters.Mono or VoicemeeterParameters.Solo,
		VoicemeeterChannelKind.Bus => option is VoicemeeterParameters.Mono
			or VoicemeeterParameters.Eq
			or VoicemeeterParameters.Sel,
		_ => false
	};

	// Push mirrors set-macro-button's "press" mode (a momentary pulse); TwoPositions mirrors its "toggle"
	// mode (a persistent state flip). Both read the button's current state the same way the source plugin
	// does, so no explicit state is carried across.
	private static ActionMigrationResult? MigrateMacroButton(ForeignAction action)
	{
		if (!TryParseObject(action.Configuration, out var config) ||
			!TryReadInt(config, "ButtonId", out var buttonId) ||
			buttonId < 0 ||
			buttonId >= VoicemeeterConnection.MacroButtonCount ||
			!TryReadInt(config, "ButtonType", out var buttonType))
		{
			return null;
		}

		var mode = buttonType switch
		{
			0 => "press",
			1 => "toggle",
			_ => null
		};

		if (mode is null)
		{
			return null;
		}

		return new ActionMigrationResult(VoicemeeterIntegration.IntegrationId,
			"set-macro-button",
			action.DisplayName ?? $"Set macro button {buttonId}",
			Parameters((SetMacroButtonActionDefinition.ButtonParameter, JsonNumber(buttonId)),
				(VoicemeeterActionValues.ModeParameter, JsonString(mode))));
	}

	private static bool TryParseChannel(string id, out VoicemeeterChannelKind kind, out int index)
	{
		kind = default;
		index = 0;

		var match = _channelId.Match(id);
		if (!match.Success)
		{
			return false;
		}

		kind = match.Groups[1].Value == "Strip" ? VoicemeeterChannelKind.Strip : VoicemeeterChannelKind.Bus;
		return int.TryParse(match.Groups[2].Value, out index);
	}

	private static bool TryParseObject(string? configuration, out JsonElement config)
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

	private static bool TryReadOption(JsonElement config, out string id, out string option)
	{
		id = string.Empty;
		option = string.Empty;

		if (!config.TryGetProperty("Option", out var optionElement) ||
			optionElement.ValueKind != JsonValueKind.Object)
		{
			return false;
		}

		if (ReadString(optionElement, "Id") is not { Length: > 0 } readId ||
			ReadString(optionElement, "Option") is not { Length: > 0 } readOption)
		{
			return false;
		}

		id = readId;
		option = readOption;
		return true;
	}

	private static bool TryReadNestedInt(JsonElement config, string objectName, string propertyName, out int value)
	{
		value = 0;
		return config.TryGetProperty(objectName, out var nested) &&
			nested.ValueKind == JsonValueKind.Object &&
			TryReadInt(nested, propertyName, out value);
	}

	private static bool TryReadInt(JsonElement config, string name, out int value)
	{
		value = 0;
		return config.TryGetProperty(name, out var element) &&
			element.ValueKind == JsonValueKind.Number &&
			element.TryGetInt32(out value);
	}

	private static bool TryReadDouble(JsonElement config, string name, out double value)
	{
		value = 0;
		return config.TryGetProperty(name, out var element) &&
			element.ValueKind == JsonValueKind.Number &&
			element.TryGetDouble(out value);
	}

	private static string? ReadString(JsonElement config, string name)
		=> config.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;

	private static JsonElement JsonString(string value) => JsonSerializer.SerializeToElement(value);

	private static JsonElement JsonNumber(double value) => JsonSerializer.SerializeToElement(value);

	private static Dictionary<string, JsonElement> Parameters(params (string Name, JsonElement Value)[] values)
		=> values.ToDictionary(entry => entry.Name, entry => entry.Value, StringComparer.Ordinal);
}
