using System.Globalization;

namespace MacroDeckHost.Integrations.System.Metrics;

internal static class NvidiaSmiParser
{
	public static double? ParseUtilization(string output)
	{
		foreach (var line in output.Split('\n'))
		{
			if (double.TryParse(line.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
			{
				return Math.Clamp(value, 0.0, 100.0);
			}
		}

		return null;
	}

	public static string? ParseName(string output)
	{
		foreach (var line in output.Split('\n'))
		{
			var name = line.Trim();
			if (name.Length > 0)
			{
				return name;
			}
		}

		return null;
	}
}
