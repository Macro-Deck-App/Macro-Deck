using System.Globalization;

namespace MacroDeckHost.Integrations.System.Metrics;

internal static class NvidiaSmiParser
{
	public static IReadOnlyList<GpuSample> ParseGpus(string output)
	{
		var byIndex = new SortedDictionary<int, GpuSample>();
		foreach (var line in output.Split('\n'))
		{
			var fields = line.Split(',');
			if (fields.Length < 3 ||
				!int.TryParse(fields[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
			{
				continue;
			}

			var name = fields[1].Trim();
			var usage = double.TryParse(fields[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
				? Math.Clamp(value, 0d, 100d)
				: (double?)null;

			byIndex[index] = new GpuSample(name.Length > 0 ? name : null, usage);
		}

		return byIndex.Values.ToArray();
	}
}
