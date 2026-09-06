using System.Globalization;

namespace MacroDeckHost.Integrations.Twitch.Actions;

internal static class TwitchActionValues
{
	public static string? ReadText(IReadOnlyDictionary<string, object> parameters, string name)
	{
		var value = parameters.GetValueOrDefault(name);
		var text = value switch
		{
			null => null,
			string s => s,
			bool b => b ? "true" : "false",
			IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
			_ => value.ToString()
		};

		return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
	}

	public static bool ReadBool(IReadOnlyDictionary<string, object> parameters, string name, bool fallback = false)
		=> parameters.GetValueOrDefault(name) switch
		{
			null => fallback,
			bool b => b,
			string s when bool.TryParse(s, out var parsed) => parsed,
			_ => fallback
		};

	public static int? ReadInt(IReadOnlyDictionary<string, object> parameters, string name)
		=> parameters.GetValueOrDefault(name) switch
		{
			null => null,
			int i => i,
			long l => (int)l,
			double d => (int)d,
			string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
			IFormattable formattable when int.TryParse(formattable.ToString(null, CultureInfo.InvariantCulture),
				NumberStyles.Integer,
				CultureInfo.InvariantCulture,
				out var parsed) => parsed,
			_ => null
		};

	public static IReadOnlyList<string> ReadLines(IReadOnlyDictionary<string, object> parameters, string name)
		=> ReadText(parameters, name)
				?.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ??
			[];

	public static IReadOnlyList<string> ReadList(IReadOnlyDictionary<string, object> parameters, string name)
		=> ReadText(parameters, name)
				?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ??
			[];

	public static string? ReadLogin(IReadOnlyDictionary<string, object> parameters, string name)
	{
		if (ReadText(parameters, name) is not { } value)
		{
			return null;
		}

		var login = value.TrimStart('@');
		var slash = login.LastIndexOf('/');
		if (slash >= 0 && slash < login.Length - 1)
		{
			login = login[(slash + 1)..];
		}

		var query = login.IndexOf('?', StringComparison.Ordinal);
		if (query >= 0)
		{
			login = login[..query];
		}

		return string.IsNullOrWhiteSpace(login) ? null : login.ToLowerInvariant();
	}
}
