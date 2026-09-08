using System.Globalization;

namespace MacroDeckHost.Integrations.System.Metrics;

internal static class WindowsGpuCounterParser
{
	private const string LuidMarker = "_luid_";
	private const string EngineTypeMarker = "_engtype_";

	internal readonly record struct EngineInstance(int LuidHigh, uint LuidLow, string EngineKey);

	// The maximum over an adapter's engines, which is the figure Task Manager reports.
	public static IReadOnlyDictionary<(int High, uint Low), double> Aggregate(IEnumerable<CounterEntry> entries)
	{
		var perEngine = new Dictionary<EngineInstance, double>();
		foreach (var entry in entries)
		{
			if (Parse(entry.InstanceName) is not { } instance)
			{
				continue;
			}

			perEngine[instance] = perEngine.GetValueOrDefault(instance) + entry.Value;
		}

		var perAdapter = new Dictionary<(int, uint), double>();
		foreach (var (instance, value) in perEngine)
		{
			var key = (instance.LuidHigh, instance.LuidLow);
			perAdapter[key] = Math.Max(perAdapter.GetValueOrDefault(key), value);
		}

		return perAdapter;
	}

	public static EngineInstance? Parse(string instanceName)
	{
		var luidStart = instanceName.IndexOf(LuidMarker, StringComparison.Ordinal);
		if (luidStart < 0)
		{
			return null;
		}

		var rest = instanceName[(luidStart + LuidMarker.Length)..];
		var separator = rest.IndexOf('_');
		if (separator <= 0)
		{
			return null;
		}

		var secondEnd = rest.IndexOf('_', separator + 1);
		var secondPart = secondEnd < 0 ? rest[(separator + 1)..] : rest[(separator + 1)..secondEnd];
		if (!TryParseHex(rest[..separator], out var high) || !TryParseHex(secondPart, out var low))
		{
			return null;
		}

		if (secondEnd < 0 || instanceName.IndexOf(EngineTypeMarker, StringComparison.Ordinal) < 0)
		{
			return null;
		}

		return new EngineInstance((int)high, low, rest[(secondEnd + 1)..]);
	}

	private static bool TryParseHex(string value, out uint result)
	{
		var digits = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
		return uint.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);
	}
}
