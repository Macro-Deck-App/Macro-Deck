using System.Globalization;

namespace MacroDeck.Sdk.Input;

/// <summary>
/// The names Macro Deck stores for keyboard keys and modifiers in <c>KeyboardCombo</c> values: what the combo
/// editor records, what Press Key reads and what trigger matching compares.
/// </summary>
public static class KeyNames
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
			["super"] = KeyModifier.Meta,
			["rightctrl"] = KeyModifier.RightControl,
			["rightcontrol"] = KeyModifier.RightControl,
			["rightshift"] = KeyModifier.RightShift,
			["rightalt"] = KeyModifier.RightAlt,
			["rightmeta"] = KeyModifier.RightMeta
		};

	/// <summary>
	/// Resolves any spelling the host accepts (<c>Minus</c>, <c>-</c>, <c>Esc</c>, <c>5</c> and <c>D5</c>, ...),
	/// ignoring case and surrounding whitespace. A name the host does not know returns <see langword="false" />.
	/// </summary>
	public static bool TryParse(string? name, out KeyCode key)
	{
		key = KeyCode.None;
		if (string.IsNullOrWhiteSpace(name))
		{
			return false;
		}

		return _keys.TryGetValue(name.Trim(), out key);
	}

	/// <summary>
	/// Resolves a modifier spelling (<c>Ctrl</c>, <c>Cmd</c>, <c>Option</c>, <c>RightShift</c>, ...), ignoring case.
	/// <c>AltGr</c> counts as <see cref="KeyModifier.Alt" />.
	/// </summary>
	public static bool TryParseModifier(string? name, out KeyModifier modifier)
	{
		modifier = KeyModifier.None;
		if (string.IsNullOrWhiteSpace(name))
		{
			return false;
		}

		return _modifiers.TryGetValue(name.Trim(), out modifier);
	}

	/// <summary>
	/// The one name the combo editor stores for a key: <c>A</c> to <c>Z</c>, <c>0</c> to <c>9</c> for the digit row,
	/// and the <see cref="KeyCode" /> name for every other key. <see cref="KeyCode.None" /> returns an empty string.
	/// </summary>
	public static string ToName(KeyCode key) => key switch
	{
		KeyCode.None => string.Empty,
		>= KeyCode.D0 and <= KeyCode.D9 => (key - KeyCode.D0).ToString(CultureInfo.InvariantCulture),
		_ => key.ToString()
	};

	/// <summary>
	/// Reports the modifier a modifier key stands for, such as <see cref="KeyCode.LeftControl" /> for
	/// <see cref="KeyModifier.Control" /> and <see cref="KeyCode.RightControl" /> for <see cref="KeyModifier.RightControl" />.
	/// Returns <see langword="false" /> for every other key.
	/// </summary>
	public static bool TryGetModifier(KeyCode key, out KeyModifier modifier)
	{
		modifier = key switch
		{
			KeyCode.LeftControl => KeyModifier.Control,
			KeyCode.RightControl => KeyModifier.RightControl,
			KeyCode.LeftShift => KeyModifier.Shift,
			KeyCode.RightShift => KeyModifier.RightShift,
			KeyCode.LeftAlt => KeyModifier.Alt,
			KeyCode.RightAlt => KeyModifier.RightAlt,
			KeyCode.LeftMeta => KeyModifier.Meta,
			KeyCode.RightMeta => KeyModifier.RightMeta,
			_ => KeyModifier.None
		};

		return modifier != KeyModifier.None;
	}

	/// <summary>
	/// The modifier names the combo editor stores for the held modifiers, in the order Ctrl, Shift, Alt, Meta. A
	/// modifier is named <c>Ctrl</c> when the left key is held, alone or together with the right one, and
	/// <c>RightCtrl</c> only when just the right key is held, like the editor records it. Trigger matching compares
	/// the two names as different modifiers. The result is lossy: <c>Control | RightControl</c> yields <c>Ctrl</c>.
	/// </summary>
	public static IReadOnlyList<string> ToModifierNames(KeyModifier modifiers)
	{
		var names = new List<string>(4);
		AddModifierName(names, modifiers, KeyModifier.Control, KeyModifier.RightControl, "Ctrl");
		AddModifierName(names, modifiers, KeyModifier.Shift, KeyModifier.RightShift, "Shift");
		AddModifierName(names, modifiers, KeyModifier.Alt, KeyModifier.RightAlt, "Alt");
		AddModifierName(names, modifiers, KeyModifier.Meta, KeyModifier.RightMeta, "Meta");
		return names;
	}

	private static void AddModifierName(List<string> names, KeyModifier held, KeyModifier left, KeyModifier right, string name)
	{
		if (held.HasFlag(left))
		{
			names.Add(name);
		}
		else if (held.HasFlag(right))
		{
			names.Add("Right" + name);
		}
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
