using System.Text.RegularExpressions;

namespace MacroDeckHost.Infrastructure.Adb;

internal static partial class AdbPropertyParsers
{
	private const string BatteryLevelMarker = "level:";

	public static int? ParseBatteryLevel(string dumpsysBatteryOutput)
	{
		foreach (var rawLine in dumpsysBatteryOutput.Split('\n'))
		{
			var line = rawLine.Trim();
			if (!line.StartsWith(BatteryLevelMarker, StringComparison.Ordinal))
			{
				continue;
			}

			var value = line[BatteryLevelMarker.Length..].Trim();
			return int.TryParse(value, out var level) && level is >= 0 and <= 100 ? level : null;
		}

		return null;
	}

	public static bool? ParseScreenOn(string dumpsysPowerOutput)
	{
		if (dumpsysPowerOutput.Contains("mWakefulness=Awake", StringComparison.Ordinal))
		{
			return true;
		}

		if (dumpsysPowerOutput.Contains("mWakefulness=Asleep", StringComparison.Ordinal) ||
			dumpsysPowerOutput.Contains("mWakefulness=Dozing", StringComparison.Ordinal))
		{
			return false;
		}

		if (dumpsysPowerOutput.Contains("Display Power: state=ON", StringComparison.Ordinal))
		{
			return true;
		}

		if (dumpsysPowerOutput.Contains("Display Power: state=OFF", StringComparison.Ordinal))
		{
			return false;
		}

		return null;
	}

	public static bool? ParseLocked(string dumpsysWindowOutput)
	{
		var match = DreamingLockscreenRegex().Match(dumpsysWindowOutput);
		return match.Success ? match.Groups[1].Value == "true" : null;
	}

	public static string? ParseForegroundPackage(string dumpsysWindowOutput)
	{
		var match = CurrentFocusRegex().Match(dumpsysWindowOutput);
		return match.Success ? match.Groups[1].Value : null;
	}

	public static string? ParseSingleLineProperty(string getpropOutput)
	{
		foreach (var rawLine in getpropOutput.Split('\n'))
		{
			var line = rawLine.Trim('\r').Trim();
			if (line.Length > 0)
			{
				return line;
			}
		}

		return null;
	}

	[GeneratedRegex("mDreamingLockscreen=(true|false)")]
	private static partial Regex DreamingLockscreenRegex();

	[GeneratedRegex(@"mCurrentFocus=Window\{.*\s(\S+)/\S+\}")]
	private static partial Regex CurrentFocusRegex();
}
