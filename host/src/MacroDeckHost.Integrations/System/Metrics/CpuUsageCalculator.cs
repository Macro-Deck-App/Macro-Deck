namespace MacroDeckHost.Integrations.System.Metrics;

internal readonly record struct CpuTimes(ulong BusyTicks, ulong TotalTicks);

internal static class CpuUsageCalculator
{
	public static double? Calculate(CpuTimes? previous, CpuTimes current)
	{
		if (previous is not { } prev)
		{
			return null;
		}

		if (current.TotalTicks <= prev.TotalTicks || current.BusyTicks < prev.BusyTicks)
		{
			return null;
		}

		var busyDelta = current.BusyTicks - prev.BusyTicks;
		var totalDelta = current.TotalTicks - prev.TotalTicks;
		return Math.Clamp(busyDelta * 100.0 / totalDelta, 0.0, 100.0);
	}
}
