using System.Globalization;

namespace MacroDeckHost.Integrations.Adb.Actions;

internal static class AdbActionValues
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
			string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
			string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedDouble)
				=> (int)Math.Round(parsedDouble),
			_ => fallback
		};
	}
}
