namespace MacroDeckHost.Application.Plugins.Runtime;

public enum PluginRuntimeState
{
	Stopped,
	Starting,
	Running,
	Stopping,
	Backoff,
	Failed
}

public enum PluginHealthState
{
	Unknown,
	Healthy,
	Degraded,
	Unhealthy,
	Crashed,
	Failed
}

public enum PluginStopReason
{
	None,
	UserRequested,
	HostShutdown,
	Update,
	ManualRestart,
	Crash,
	HealthFailure,
	LaunchFailure
}
