namespace MacroDeckHost.Integrations.System.Metrics;

internal static class DiskRateCalculator
{
	public static DiskActivity? Calculate(DiskCounters? previous, DiskCounters current, TimeSpan elapsed)
	{
		if (previous is not { } prev ||
			elapsed <= TimeSpan.Zero ||
			current.ReadBytes < prev.ReadBytes ||
			current.WriteBytes < prev.WriteBytes)
		{
			return null;
		}

		var seconds = elapsed.TotalSeconds;
		return new DiskActivity((current.ReadBytes - prev.ReadBytes) / seconds,
			(current.WriteBytes - prev.WriteBytes) / seconds,
			ActivePercent(prev.ReadTime, current.ReadTime, elapsed),
			ActivePercent(prev.WriteTime, current.WriteTime, elapsed));
	}

	public static double ClampPercent(double value) => double.IsFinite(value) ? Math.Clamp(value, 0d, 100d) : 0d;

	private static double? ActivePercent(TimeSpan? previous, TimeSpan? current, TimeSpan elapsed)
	{
		if (previous is not { } prev || current is not { } now || now < prev)
		{
			return null;
		}

		return ClampPercent((now - prev) / elapsed * 100d);
	}
}
