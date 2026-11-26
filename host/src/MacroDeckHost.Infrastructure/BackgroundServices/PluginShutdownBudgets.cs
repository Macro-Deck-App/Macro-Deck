namespace MacroDeckHost.Infrastructure.BackgroundServices;

public static class PluginShutdownBudgets
{
	public static readonly TimeSpan SupervisorPluginBudget = TimeSpan.FromSeconds(25);

	public static readonly TimeSpan HostShutdownTimeout = TimeSpan.FromSeconds(30);

	public static readonly TimeSpan HostExitDeadline = HostShutdownTimeout + TimeSpan.FromSeconds(10);
}
