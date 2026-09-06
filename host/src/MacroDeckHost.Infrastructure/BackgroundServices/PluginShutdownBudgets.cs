namespace MacroDeckHost.Infrastructure.BackgroundServices;

public static class PluginShutdownBudgets
{
	public static readonly TimeSpan SupervisorPluginBudget = TimeSpan.FromSeconds(25);

	public static readonly TimeSpan HostShutdownTimeout = TimeSpan.FromSeconds(30);
}
