namespace MacroDeckHost.Application.Plugins.Runtime;

public sealed class PluginSupervisorOptions
{
	public static readonly PluginSupervisorOptions Default = new();

	public TimeSpan GracefulShutdownTimeout { get; init; } = TimeSpan.FromSeconds(10);

	public TimeSpan HealthPollInterval { get; init; } = TimeSpan.FromSeconds(15);

	public TimeSpan HealthTimeout { get; init; } = TimeSpan.FromSeconds(2);

	public int UnhealthyThreshold { get; init; } = 3;

	public TimeSpan StartupGrace { get; init; } = TimeSpan.FromSeconds(30);

	public TimeSpan StableRuntime { get; init; } = TimeSpan.FromMinutes(2);

	public int MaxRestarts { get; init; } = 5;

	public TimeSpan RestartWindow { get; init; } = TimeSpan.FromMinutes(10);

	public int BootstrapOutputLines { get; init; } = 100;

	public int BootstrapOutputBytes { get; init; } = 16 * 1024;
}
