using System.Globalization;

namespace MacroDeckHost.Integrations.Keyboard.Native;

public sealed class KeyboardLayoutService : IKeyboardLayoutService
{
	private static readonly Dictionary<string, KeyCode> _keys = BuildKeyMap();

	private static readonly Dictionary<string, KeyModifier> _modifiers =
		new(StringComparer.OrdinalIgnoreCase)
		{
			["ctrl"] = KeyModifier.Control,
			["control"] = KeyModifier.Control,
			["ctl"] = KeyModifier.Control,
			["shift"] = KeyModifier.Shift,
			["alt"] = KeyModifier.Alt,
			["option"] = KeyModifier.Alt,
			["opt"] = KeyModifier.Alt,
			["altgr"] = KeyModifier.Alt,
			["meta"] = KeyModifier.Meta,
			["cmd"] = KeyModifier.Meta,
			["command"] = KeyModifier.Meta,
			["win"] = KeyModifier.Meta,
			["windows"] = KeyModifier.Meta,
			["super"] = KeyModifier.Meta
		};

	public bool TryResolveKey(string name, out KeyCode key)
	{
		key = KeyCode.None;
		if (string.IsNullOrWhiteSpace(name))
		{
			return false;
		}

		return _keys.TryGetValue(name.Trim(), out key);
	}

	public bool TryResolveModifier(string name, out KeyModifier modifier)
	{
		modifier = KeyModifier.None;
		if (string.IsNullOrWhiteSpace(name))
		{
			return false;
		}

		return _modifiers.TryGetValue(name.Trim(), out modifier);
	}

	public KeyModifier ResolveModifiers(IEnumerable<string> names)
	{
		var result = KeyModifier.None;
		foreach (var name in names)
		{
			if (TryResolveModifier(name, out var modifier))
			{
				result |= modifier;
			}
		}

		return result;
	}

	public IReadOnlyList<KeyCode> ExpandModifiers(KeyModifier modifiers)
	{
		var result = new List<KeyCode>(4);
		if (modifiers.HasFlag(KeyModifier.Control))
		{
			result.Add(KeyCode.LeftControl);
		}

		if (modifiers.HasFlag(KeyModifier.Shift))
		{
			result.Add(KeyCode.LeftShift);
		}

		if (modifiers.HasFlag(KeyModifier.Alt))
		{
			result.Add(KeyCode.LeftAlt);
		}

		if (modifiers.HasFlag(KeyModifier.Meta))
		{
			result.Add(KeyCode.LeftMeta);
		}

		return result;
	}

	private static Dictionary<string, KeyCode> BuildKeyMap()
	{
		var map = new Dictionary<string, KeyCode>(StringComparer.OrdinalIgnoreCase);

		foreach (var code in Enum.GetValues<KeyCode>())
		{
			if (code != KeyCode.None)
			{
				map[code.ToString()] = code;
			}
		}

		for (var d = 0; d <= 9; d++)
		{
			var digit = d.ToString(CultureInfo.InvariantCulture);
			map[digit] = Enum.Parse<KeyCode>($"D{digit}");
		}

		AddAliases(map, KeyCode.Enter, "Return");
		AddAliases(map, KeyCode.Escape, "Esc");
		AddAliases(map, KeyCode.Backspace, "Back");
		AddAliases(map, KeyCode.Space, "Spacebar", " ");
		AddAliases(map, KeyCode.Delete, "Del");
		AddAliases(map, KeyCode.Insert, "Ins");
		AddAliases(map, KeyCode.PageUp, "PgUp");
		AddAliases(map, KeyCode.PageDown, "PgDn");
		AddAliases(map, KeyCode.ArrowUp, "Up");
		AddAliases(map, KeyCode.ArrowDown, "Down");
		AddAliases(map, KeyCode.ArrowLeft, "Left");
		AddAliases(map, KeyCode.ArrowRight, "Right");
		AddAliases(map, KeyCode.LeftControl, "Ctrl", "Control");
		AddAliases(map, KeyCode.LeftShift, "Shift");
		AddAliases(map, KeyCode.LeftAlt, "Alt", "Option");
		AddAliases(map, KeyCode.LeftMeta, "Meta", "Cmd", "Command", "Win", "Windows", "Super");
		AddAliases(map, KeyCode.Semicolon, ";");
		AddAliases(map, KeyCode.Equal, "=");
		AddAliases(map, KeyCode.Comma, ",");
		AddAliases(map, KeyCode.Minus, "-");
		AddAliases(map, KeyCode.Period, ".");
		AddAliases(map, KeyCode.Slash, "/");
		AddAliases(map, KeyCode.Backquote, "`");
		AddAliases(map, KeyCode.BracketLeft, "[");
		AddAliases(map, KeyCode.Backslash, "\\");
		AddAliases(map, KeyCode.BracketRight, "]");
		AddAliases(map, KeyCode.Quote, "'");

		AddAliases(map, KeyCode.MediaTrackNext, "MediaNextTrack", "MediaNext");
		AddAliases(map, KeyCode.MediaTrackPrevious, "MediaPreviousTrack", "MediaPrevious", "MediaPrev");
		AddAliases(map, KeyCode.AudioVolumeUp, "VolumeUp");
		AddAliases(map, KeyCode.AudioVolumeDown, "VolumeDown");
		AddAliases(map, KeyCode.AudioVolumeMute, "VolumeMute", "Mute");

		return map;
	}

	private static void AddAliases(Dictionary<string, KeyCode> map, KeyCode code, params string[] aliases)
	{
		foreach (var alias in aliases)
		{
			map[alias] = code;
		}
	}
}
