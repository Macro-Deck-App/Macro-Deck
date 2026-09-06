using System.Globalization;

namespace MacroDeckHost.Integrations.System.Metrics;

internal static class LinuxMetricsParser
{
	public static CpuTimes? ParseProcStat(string content)
	{
		foreach (var line in content.Split('\n'))
		{
			if (!line.StartsWith("cpu ", StringComparison.Ordinal))
			{
				continue;
			}

			var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			if (fields.Length < 5)
			{
				return null;
			}

			var values = new ulong[Math.Min(fields.Length - 1, 8)];
			for (var i = 0; i < values.Length; i++)
			{
				if (!ulong.TryParse(fields[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out values[i]))
				{
					return null;
				}
			}

			var total = 0ul;
			foreach (var value in values)
			{
				total += value;
			}

			var idle = values[3] + (values.Length > 4 ? values[4] : 0ul);
			return new CpuTimes(total - idle, total);
		}

		return null;
	}

	public static MemoryInfo? ParseMemInfo(string content)
	{
		long? totalKb = null;
		long? availableKb = null;

		foreach (var line in content.Split('\n'))
		{
			if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
			{
				totalKb = ParseMemInfoValue(line);
			}
			else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal))
			{
				availableKb = ParseMemInfoValue(line);
			}

			if (totalKb is not null && availableKb is not null)
			{
				break;
			}
		}

		if (totalKb is not > 0 || availableKb is not { } available)
		{
			return null;
		}

		return new MemoryInfo(totalKb.Value * 1024, available * 1024);
	}

	public static double? ParseGpuBusyPercent(string content)
	{
		if (!double.TryParse(content.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
		{
			return null;
		}

		return Math.Clamp(value, 0.0, 100.0);
	}

	private static long? ParseMemInfoValue(string line)
	{
		var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (fields.Length < 2 ||
			!long.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var value))
		{
			return null;
		}

		return value;
	}
}
