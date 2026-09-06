using System.Globalization;

namespace MacroDeckHost.Integrations.System.Actions;

internal static class SystemActionValues
{
	public static string ReadString(IReadOnlyDictionary<string, object> parameters, string name)
		=> parameters.TryGetValue(name, out var value) && value is not null
			? value.ToString() ?? string.Empty
			: string.Empty;

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

	public static double ReadDouble(IReadOnlyDictionary<string, object> parameters, string name, double fallback)
	{
		if (!parameters.TryGetValue(name, out var value) || value is null)
		{
			return fallback;
		}

		return value switch
		{
			double d => d,
			int i => i,
			long l => l,
			float f => f,
			string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
				=> parsed,
			_ => fallback
		};
	}

	public static bool ReadBool(IReadOnlyDictionary<string, object> parameters, string name, bool fallback)
	{
		if (!parameters.TryGetValue(name, out var value) || value is null)
		{
			return fallback;
		}

		return value switch
		{
			bool b => b,
			int i => i != 0,
			long l => l != 0,
			string s when bool.TryParse(s, out var parsed) => parsed,
			_ => fallback
		};
	}
}
