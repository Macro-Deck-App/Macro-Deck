using System.Globalization;
using System.Text.RegularExpressions;

namespace MacroDeckHost.Integrations.System.Metrics;

internal static partial class MacOsIoRegParser
{
	[GeneratedRegex("\"Device Utilization %\"\\s*=\\s*(\\d+)")]
	private static partial Regex DeviceUtilizationRegex();

	[GeneratedRegex("\"model\"\\s*=\\s*<?\"([^\"]+)\"")]
	private static partial Regex ModelRegex();

	public static double? ParseDeviceUtilization(string output)
	{
		var match = DeviceUtilizationRegex().Match(output);
		if (!match.Success ||
			!double.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
		{
			return null;
		}

		return Math.Clamp(value, 0.0, 100.0);
	}

	public static string? ParseAcceleratorModel(string output)
	{
		var match = ModelRegex().Match(output);
		if (!match.Success)
		{
			return null;
		}

		var value = match.Groups[1].Value.Trim();
		return value.Length > 0 ? value : null;
	}
}
