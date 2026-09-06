using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.Migration;
using MacroDeckHost.Integrations.Keyboard;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.System;

/// <summary>
/// Translates Macro Deck 2's "Windows Utils" plugin. Its actions land on four different Macro Deck 3
/// integrations (keyboard, mouse, system, http), but only one migrator may claim a foreign assembly, so
/// this one lives on <see cref="SystemIntegration" /> and names the target integration explicitly per
/// action instead of assuming its own.
/// </summary>
internal sealed class WindowsUtilsMacroDeck2Migration : IIntegrationMigration
{
	public MigrationSource Source => MigrationSource.MacroDeck2;

	private const string KeyboardIntegrationId = "app.macro-deck.keyboard";

	// The plugin's csproj sets no AssemblyName, so the SDK defaults it to the project file name ("Windows
	// Utils.csproj"), also the "dll" field of its ExtensionManifest.json minus the extension - confirmed
	// against a real profile, whose $type strings read "..., Windows Utils".
	public IReadOnlyList<string> ClaimedActionSources { get; } = ["Windows Utils"];

	// Macro Deck 2 derives its settings/credentials file name from the plugin's author ("Macro Deck") and
	// display name ("Windows Utils"), lowercased and joined with an underscore.
	public IReadOnlyList<string> ClaimedSettingsSources { get; } = ["macro deck_windows utils"];

	public Task<ActionMigrationResult?> MigrateActionAsync(ForeignAction action, CancellationToken cancellationToken)
		=> Task.FromResult(Migrate(action));

	public Task<IReadOnlyList<MigratedConfiguration>> MigrateConfigurationAsync(
		ForeignPluginSettings settings,
		CancellationToken cancellationToken)
		=> Task.FromResult(MigrateConfiguration(settings));

	private static ActionMigrationResult? Migrate(ForeignAction action) => action.TypeName switch
	{
		"SuchByte.WindowsUtils.Actions.HotkeyAction" => MigrateHotkey(action),
		"SuchByte.WindowsUtils.Actions.WriteTextAction" => MigrateWriteText(action),
		"SuchByte.WindowsUtils.Actions.StartApplicationAction" => MigrateStartApplication(action),
		"SuchByte.WindowsUtils.Actions.OpenFileAction" => MigratePath(action, "open-file"),
		"SuchByte.WindowsUtils.Actions.OpenFolderAction" => MigratePath(action, "open-folder"),
		"SuchByte.WindowsUtils.Actions.CommandlineAction" => MigrateCommandline(action),
		"SuchByte.WindowsUtils.Actions.MuteVolumeAction" => NoParameterAction(action, "mute-volume", "Mute volume"),
		"SuchByte.WindowsUtils.Actions.IncreaseVolumeAction" => NoParameterAction(action,
			"increase-volume",
			"Increase volume"),
		"SuchByte.WindowsUtils.Actions.DecreaseVolumeAction" => NoParameterAction(action,
			"decrease-volume",
			"Decrease volume"),

		// MultiHotkeyAction's own config UI is wired to StartApplicationActionConfigView rather than its
		// own MultiHotkeyActionConfigView - a bug in the source plugin - so a real installation's saved
		// configuration for this action is not trustworthy. Its model also stores each step behind the
		// IMultiHotkeyAction interface, which System.Text.Json serializes with no members of the concrete
		// step type, so even a correctly configured button would not round-trip its steps. Nothing here can
		// be read with any confidence, so it is left as a placeholder.
		"SuchByte.WindowsUtils.Actions.MultiHotkeyAction" => null,

		// Simulates the browser-navigation keys (back/forward/home/refresh) for File Explorer. Macro Deck 3's
		// keyboard action has no key codes for them and no integration offers Explorer navigation directly.
		"SuchByte.WindowsUtils.Actions.WindowsExplorerControlAction" => null,
		_ => null
	};

	private static IReadOnlyList<MigratedConfiguration> MigrateConfiguration(ForeignPluginSettings settings) => [];

	private static ActionMigrationResult NoParameterAction(ForeignAction action, string actionId, string fallbackLabel)
		=> new(SystemIntegration.IntegrationId, actionId, action.DisplayName ?? fallbackLabel, Parameters());

	private static ActionMigrationResult? MigrateHotkey(ForeignAction action)
	{
		if (!TryParseObject(action.Configuration, out var config))
		{
			return null;
		}

		var keyName = ReadString(config, "key");
		if (keyName is null || !TryMapKey(keyName, out var key))
		{
			return null;
		}

		var modifiers = new List<string>();
		var warnings = new List<LocalizedText>();
		AddModifier(config, modifiers, warnings, "ctrl", "lctrl", "rctrl", "ctrl", "Ctrl");
		AddModifier(config, modifiers, warnings, "shift", "lshift", "rshift", "shift", "Shift");
		AddModifier(config, modifiers, warnings, "alt", "lalt", "ralt", "alt", "Alt");
		AddModifier(config, modifiers, warnings, null, "lwin", "rwin", "meta", "Windows");

		var parameters = Parameters(("combo", ComboJson(modifiers, key)));
		return new ActionMigrationResult(KeyboardIntegrationId,
			"press-key",
			action.DisplayName ?? "Hotkey",
			parameters,
			warnings.Count > 0 ? warnings : null);
	}

	private static void AddModifier(
		JsonElement config,
		List<string> modifiers,
		List<LocalizedText> warnings,
		string? genericProperty,
		string leftProperty,
		string rightProperty,
		string modifierName,
		string modifierDisplayName)
	{
		var generic = genericProperty is not null && ReadBool(config, genericProperty);
		var left = ReadBool(config, leftProperty);
		var right = ReadBool(config, rightProperty);

		if (!generic && !left && !right)
		{
			return;
		}

		modifiers.Add(modifierName);

		// Macro Deck 3's hotkey combo has no left/right distinction and always presses the left key, so a
		// hotkey that specifically required the right-hand modifier no longer does.
		if (right && !left && !generic)
		{
			warnings.Add(
				AppStrings.Migration.Warning.Keyboard.RightModifierNotSupported(modifier: modifierDisplayName));
		}
	}

	private static ActionMigrationResult? MigrateWriteText(ForeignAction action)
	{
		if (!TryParseObject(action.Configuration, out var config))
		{
			return null;
		}

		var text = ReadString(config, "text");
		if (text is null)
		{
			return null;
		}

		return new ActionMigrationResult(KeyboardIntegrationId,
			"type-text",
			action.DisplayName ?? "Write text",
			Parameters(("text", JsonString(text))));
	}

	private static ActionMigrationResult? MigrateStartApplication(ForeignAction action)
	{
		if (!TryParseObject(action.Configuration, out var config))
		{
			return null;
		}

		var path = ReadString(config, "path");
		if (string.IsNullOrWhiteSpace(path))
		{
			return null;
		}

		var parameters = new List<(string Name, JsonElement Value)> { ("path", JsonString(path)) };

		var arguments = ReadString(config, "arguments");
		if (!string.IsNullOrEmpty(arguments))
		{
			parameters.Add(("arguments", JsonString(arguments)));
		}

		parameters.Add(("mode",
			JsonString(ReadInt(config, "StartMethod", 0) switch
			{
				1 => "start-stop",
				2 => "start-focus",
				_ => "start"
			})));

		if (ReadBool(config, "RunAsAdmin"))
		{
			parameters.Add(("runAsAdmin", JsonSerializer.SerializeToElement(true)));
		}

		List<LocalizedText>? warnings = null;
		if (ReadBool(config, "SyncButtonState"))
		{
			warnings = [AppStrings.Migration.Warning.System.LaunchSyncNotMigrated()];
		}

		return new ActionMigrationResult(SystemIntegration.IntegrationId,
			"launch-application",
			action.DisplayName ?? "Start application",
			Parameters(parameters.ToArray()),
			warnings);
	}

	private static ActionMigrationResult? MigratePath(ForeignAction action, string actionId)
	{
		if (!TryParseObject(action.Configuration, out var config))
		{
			return null;
		}

		var path = ReadString(config, "path");
		if (string.IsNullOrWhiteSpace(path))
		{
			return null;
		}

		return new ActionMigrationResult(SystemIntegration.IntegrationId,
			actionId,
			action.DisplayName ?? (actionId == "open-file" ? "Open file" : "Open folder"),
			Parameters(("path", JsonString(path))));
	}

	private static ActionMigrationResult? MigrateCommandline(ForeignAction action)
	{
		if (!TryParseObject(action.Configuration, out var config))
		{
			return null;
		}

		var command = ReadString(config, "command");
		if (string.IsNullOrWhiteSpace(command))
		{
			return null;
		}

		var parameters = new List<(string Name, JsonElement Value)>
		{
			("shell", JsonString("default")), ("command", JsonString(command))
		};

		var workingDirectory = ReadString(config, "workingDirectory");
		if (!string.IsNullOrEmpty(workingDirectory))
		{
			parameters.Add(("workingDirectory", JsonString(workingDirectory)));
		}

		List<LocalizedText>? warnings = null;
		if (ReadBool(config, "saveVariable"))
		{
			var variableName = ReadString(config, "variableName");
			if (!string.IsNullOrEmpty(variableName))
			{
				parameters.Add(("outputVariable", JsonString(variableName)));
			}

			var variableType = ReadString(config, "variableType");
			if (!string.IsNullOrEmpty(variableType) &&
				!string.Equals(variableType, "String", StringComparison.OrdinalIgnoreCase))
			{
				warnings = [AppStrings.Migration.Warning.System.CommandOutputTypeIgnored(variableType: variableType)];
			}
		}

		return new ActionMigrationResult(SystemIntegration.IntegrationId,
			"run-command",
			action.DisplayName ?? "Run command",
			Parameters(parameters.ToArray()),
			warnings);
	}

	/// <summary>
	/// Maps H.InputSimulator's <c>VirtualKeyCode</c> names, which is what Macro Deck 2's hotkey editor
	/// stored verbatim, onto Macro Deck 3's <see cref="KeyCode" />. Anything not in this table (browser and
	/// launch keys, gamepad input, IME keys, mouse buttons) has no counterpart and is left unmapped so the
	/// caller can fall back to a placeholder rather than binding the wrong key.
	/// </summary>
	private static bool TryMapKey(string virtualKeyCode, out string key)
	{
		key = _keyMap.GetValueOrDefault(virtualKeyCode, string.Empty);
		return key.Length > 0;
	}

	private static readonly Dictionary<string, string> _keyMap = BuildKeyMap();

	// Only VirtualKeyCode's letters and digits carry a "VK_" prefix - every other member (RETURN, HOME, F12,
	// OEM_1, ...) is bare, confirmed against a real Macro Deck 2 profile whose HotkeyAction configuration
	// stored "key": "RETURN" and "key": "F12" with no prefix. Getting this wrong silently drops every
	// hotkey that is not a letter, digit or function key.
	private static Dictionary<string, string> BuildKeyMap()
	{
		var map = new Dictionary<string, string>(StringComparer.Ordinal);

		foreach (var letter in "ABCDEFGHIJKLMNOPQRSTUVWXYZ")
		{
			map[$"VK_{letter}"] = letter.ToString();
		}

		for (var digit = 0; digit <= 9; digit++)
		{
			map[$"VK_{digit}"] = $"D{digit}";
		}

		for (var f = 1; f <= 24; f++)
		{
			map[$"F{f}"] = $"F{f}";
		}

		map["RETURN"] = "Enter";
		map["ESCAPE"] = "Escape";
		map["BACK"] = "Backspace";
		map["TAB"] = "Tab";
		map["SPACE"] = "Space";
		map["CAPITAL"] = "CapsLock";
		map["INSERT"] = "Insert";
		map["DELETE"] = "Delete";
		map["HOME"] = "Home";
		map["END"] = "End";
		map["PRIOR"] = "PageUp";
		map["NEXT"] = "PageDown";
		map["SNAPSHOT"] = "PrintScreen";
		map["SCROLL"] = "ScrollLock";
		map["PAUSE"] = "Pause";
		map["UP"] = "ArrowUp";
		map["DOWN"] = "ArrowDown";
		map["LEFT"] = "ArrowLeft";
		map["RIGHT"] = "ArrowRight";
		map["SHIFT"] = "LeftShift";
		map["LSHIFT"] = "LeftShift";
		map["RSHIFT"] = "RightShift";
		map["CONTROL"] = "LeftControl";
		map["LCONTROL"] = "LeftControl";
		map["RCONTROL"] = "RightControl";
		map["MENU"] = "LeftAlt";
		map["LMENU"] = "LeftAlt";
		map["RMENU"] = "RightAlt";
		map["LWIN"] = "LeftMeta";
		map["RWIN"] = "RightMeta";
		map["NUMLOCK"] = "NumLock";
		for (var digit = 0; digit <= 9; digit++)
		{
			map[$"NUMPAD{digit}"] = $"Numpad{digit}";
		}

		map["ADD"] = "NumpadAdd";
		map["SUBTRACT"] = "NumpadSubtract";
		map["MULTIPLY"] = "NumpadMultiply";
		map["DIVIDE"] = "NumpadDivide";
		map["DECIMAL"] = "NumpadDecimal";
		map["OEM_1"] = "Semicolon";
		map["OEM_PLUS"] = "Equal";
		map["OEM_COMMA"] = "Comma";
		map["OEM_MINUS"] = "Minus";
		map["OEM_PERIOD"] = "Period";
		map["OEM_2"] = "Slash";
		map["OEM_3"] = "Backquote";
		map["OEM_4"] = "BracketLeft";
		map["OEM_5"] = "Backslash";
		map["OEM_6"] = "BracketRight";
		map["OEM_7"] = "Quote";
		map["MEDIA_PLAY_PAUSE"] = "MediaPlayPause";
		map["MEDIA_STOP"] = "MediaStop";
		map["MEDIA_NEXT_TRACK"] = "MediaTrackNext";
		map["MEDIA_PREV_TRACK"] = "MediaTrackPrevious";
		map["VOLUME_UP"] = "AudioVolumeUp";
		map["VOLUME_DOWN"] = "AudioVolumeDown";
		map["VOLUME_MUTE"] = "AudioVolumeMute";

		return map;
	}

	private static JsonElement ComboJson(IReadOnlyList<string> modifiers, string key)
		=> JsonSerializer.SerializeToElement(new { modifiers, key });

	private static bool TryParseObject(string? json, out JsonElement element)
	{
		element = default;
		if (string.IsNullOrWhiteSpace(json))
		{
			return false;
		}

		try
		{
			element = JsonDocument.Parse(json).RootElement.Clone();
			return element.ValueKind == JsonValueKind.Object;
		}
		catch (JsonException)
		{
			return false;
		}
	}

	private static string? ReadString(JsonElement config, string name)
	{
		if (!config.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
		{
			return null;
		}

		var text = value.GetString();
		return string.IsNullOrEmpty(text) ? null : text;
	}

	// HotkeyAction and CommandlineAction build their configuration with Newtonsoft's JObject and assign
	// bool.ToString() into it, so every flag is stored as the JSON string "True"/"False" rather than a
	// JSON boolean; StartApplicationAction serializes with System.Text.Json instead and writes real JSON
	// booleans. Both are accepted here since nothing about a plugin's type name says which one wrote it.
	private static bool ReadBool(JsonElement config, string name)
	{
		if (!config.TryGetProperty(name, out var value))
		{
			return false;
		}

		return value.ValueKind switch
		{
			JsonValueKind.True => true,
			JsonValueKind.False => false,
			JsonValueKind.String => bool.TryParse(value.GetString(), out var parsed) && parsed,
			_ => false
		};
	}

	private static int ReadInt(JsonElement config, string name, int fallback)
		=> config.TryGetProperty(name, out var value) &&
			value.ValueKind == JsonValueKind.Number &&
			value.TryGetInt32(out var parsed)
				? parsed
				: fallback;

	private static JsonElement JsonString(string value) => JsonSerializer.SerializeToElement(value);

	private static Dictionary<string, JsonElement> Parameters(params (string Name, JsonElement Value)[] values)
		=> values.ToDictionary(entry => entry.Name, entry => entry.Value, StringComparer.Ordinal);
}
