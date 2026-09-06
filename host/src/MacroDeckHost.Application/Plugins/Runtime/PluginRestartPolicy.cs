using MacroDeck.Plugin.Protocol.Reconnection;

namespace MacroDeckHost.Application.Plugins.Runtime;

public static class PluginRestartPolicy
{
	public static TimeSpan DelayFor(int attempt, double jitterSample)
		=> attempt < 1 ? TimeSpan.Zero : ReconnectPolicy.DelayFor(attempt, jitterSample);

	public static bool IsBudgetExhausted(
		IReadOnlyCollection<DateTimeOffset> restartTimestamps,
		DateTimeOffset now,
		PluginSupervisorOptions options)
	{
		var withinWindow = restartTimestamps.Count(timestamp => now - timestamp <= options.RestartWindow);
		return withinWindow >= options.MaxRestarts;
	}

	public static bool IsRuntimeStable(TimeSpan runningFor, PluginSupervisorOptions options)
		=> runningFor >= options.StableRuntime;
}
