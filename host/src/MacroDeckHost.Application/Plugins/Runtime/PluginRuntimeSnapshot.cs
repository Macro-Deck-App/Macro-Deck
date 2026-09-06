namespace MacroDeckHost.Application.Plugins.Runtime;

public sealed record PluginRuntimeSnapshot
{
	public const string UnknownVersion = "0.0.0";

	public required string PluginId { get; init; }

	public required string DisplayName { get; init; }

	public required string Version { get; init; }

	public required PluginRuntimeState State { get; init; }

	public required PluginHealthState Health { get; init; }

	public required bool Managed { get; init; }

	public int? ProcessId { get; init; }

	public string? LaunchId { get; init; }

	public DateTimeOffset? StartedAt { get; init; }

	public int? LastExitCode { get; init; }

	public PluginStopReason LastStopReason { get; init; }

	public DateTimeOffset? LastExitAt { get; init; }

	public DateTimeOffset? LastHeartbeatAt { get; init; }

	public DateTimeOffset? LastHealthCheckAt { get; init; }

	public int ConsecutiveHealthFailures { get; init; }

	public int RestartCount { get; init; }

	public DateTimeOffset? NextRestartAt { get; init; }

	public string? LastError { get; init; }

	public IReadOnlyList<string> BootstrapOutput { get; init; } = [];
}
