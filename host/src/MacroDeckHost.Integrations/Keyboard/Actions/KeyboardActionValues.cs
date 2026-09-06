using System.Globalization;
using System.Text.Json;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Keyboard.Actions;

internal static class KeyboardActionValues
{
	private static readonly JsonSerializerOptions _jsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	public static (IReadOnlyList<string> Modifiers, string Key) ReadHotkey(
		IReadOnlyDictionary<string, object> parameters,
		string name)
	{
		if (!parameters.TryGetValue(name, out var value) || value is null)
		{
			return ([], string.Empty);
		}

		return value switch
		{
			IDictionary<string, object?> dict => FromDictionary(dict),
			string json when json.TrimStart().StartsWith('{') => FromJson(json),
			_ => ([], string.Empty)
		};
	}

	public static string ReadString(IReadOnlyDictionary<string, object> parameters, string name)
	{
		parameters.TryGetValue(name, out var value);
		return value?.ToString() ?? string.Empty;
	}

	public static KeyboardTarget ReadTarget(IReadOnlyDictionary<string, object> parameters)
	{
		var process = ReadString(parameters, "targetProcess").Trim();
		if (process.Length == 0)
		{
			return KeyboardTarget.None;
		}

		var mode = ReadString(parameters, "targetMode").Trim().ToLowerInvariant() switch
		{
			"focus-send" => KeyboardTargetMode.FocusThenSend,
			"background" => KeyboardTargetMode.Background,
			_ => KeyboardTargetMode.WhenFocused
		};

		return new KeyboardTarget(process, mode);
	}

	public static int ReadInt(IReadOnlyDictionary<string, object> parameters, string name, int fallback)
	{
		if (!parameters.TryGetValue(name, out var value) || value is null)
		{
			return fallback;
		}

		return value switch
		{
			int i => i,
			long l => (int)l,
			double d => (int)Math.Round(d),
			string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
				=> parsed,
			string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedDouble)
				=> (int)Math.Round(parsedDouble),
			_ => fallback
		};
	}

	public static ActionResult SessionUnavailableResult(KeyboardSessionUnavailableReason? reason) => reason switch
	{
		KeyboardSessionUnavailableReason.NotFocused => ActionResult.Success(),
		KeyboardSessionUnavailableReason.TargetNotFound => ActionResult.Failed(ActionErrorCodes.NotFound,
			AppStrings.Integrations.Keyboard.Errors.TargetApplicationNotFound()),
		KeyboardSessionUnavailableReason.FocusFailed => ActionResult.Failed(ActionErrorCodes.PermissionDenied,
			AppStrings.Integrations.Keyboard.Errors.TargetApplicationFocusFailed()),
		_ => ActionResult.Failed(ActionErrorCodes.Unavailable,
			AppStrings.Integrations.Keyboard.Errors.TargetingModeNotSupported())
	};

	private static (IReadOnlyList<string>, string) FromDictionary(IDictionary<string, object?> dict)
	{
		var modifiers = new List<string>();
		if (dict.TryGetValue("modifiers", out var rawModifiers) && rawModifiers is IEnumerable<object?> list)
		{
			modifiers.AddRange(list.Select(m => m?.ToString() ?? string.Empty)
				.Where(m => !string.IsNullOrWhiteSpace(m)));
		}

		var key = dict.TryGetValue("key", out var rawKey) ? rawKey?.ToString() ?? string.Empty : string.Empty;
		return (modifiers, key);
	}

	private static (IReadOnlyList<string>, string) FromJson(string json)
	{
		try
		{
			var hotkey = JsonSerializer.Deserialize<HotkeyDto>(json, _jsonOptions);
			return hotkey is null ? ([], string.Empty) : (hotkey.Modifiers ?? [], hotkey.Key ?? string.Empty);
		}
		catch (JsonException)
		{
			return ([], string.Empty);
		}
	}

	private sealed class HotkeyDto
	{
		public List<string>? Modifiers { get; init; }
		public string? Key { get; init; }
	}
}
