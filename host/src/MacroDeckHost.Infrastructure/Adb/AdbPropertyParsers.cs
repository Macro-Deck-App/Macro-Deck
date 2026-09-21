using System.Text.RegularExpressions;
using MacroDeckHost.Application.Adb;

namespace MacroDeckHost.Infrastructure.Adb;

internal static partial class AdbPropertyParsers
{
	private const string BatteryLevelMarker = "level:";

	private static readonly string[] _powerSources = ["AC powered", "USB powered", "Wireless powered", "Dock powered"];

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

	public static AdbBatteryReading? ParseBatteryState(string dumpsysBatteryOutput)
	{
		var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (var rawLine in dumpsysBatteryOutput.Split('\n'))
		{
			var line = rawLine.Trim();
			var separator = line.IndexOf(':', StringComparison.Ordinal);
			if (separator > 0)
			{
				values.TryAdd(line[..separator].Trim(), line[(separator + 1)..].Trim());
			}
		}

		if (!TryInt(values, "level", out var level))
		{
			return null;
		}

		if (TryInt(values, "scale", out var scale) && scale > 0)
		{
			level = level * 100 / scale;
		}

		var pluggedIn = _powerSources.Any(key => values.TryGetValue(key, out var value) && bool.TryParse(value, out var powered) && powered);

		var status = TryInt(values, "status", out var statusCode)
			? statusCode switch
			{
				2 => AdbBatteryStatus.Charging,
				3 => AdbBatteryStatus.Discharging,
				4 => AdbBatteryStatus.NotCharging,
				5 => AdbBatteryStatus.Full,
				_ => AdbBatteryStatus.Unknown
			}
			: AdbBatteryStatus.Unknown;

		var health = TryInt(values, "health", out var healthCode)
			? healthCode switch
			{
				2 => AdbBatteryHealth.Good,
				3 => AdbBatteryHealth.Overheat,
				4 => AdbBatteryHealth.Dead,
				5 => AdbBatteryHealth.OverVoltage,
				6 => AdbBatteryHealth.Failure,
				7 => AdbBatteryHealth.Cold,
				_ => AdbBatteryHealth.Unknown
			}
			: AdbBatteryHealth.Unknown;

		return new AdbBatteryReading(Math.Clamp(level, 0, 100), pluggedIn, status, health);
	}

	private static bool TryInt(Dictionary<string, string> values, string key, out int value)
	{
		value = 0;
		return values.TryGetValue(key, out var text) &&
			int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out value);
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
