using MacroDeckHost.Application.Plugins.Runtime;
using MacroDeckHost.Application.Ui.Transport.Messages;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Controllers;

public record PluginRuntimeSnapshotBody(
	string PluginId,
	string DisplayName,
	string Version,
	string State,
	string Health,
	bool Managed,
	int? ProcessId,
	string? LaunchId,
	DateTimeOffset? StartedAt,
	int? LastExitCode,
	string LastStopReason,
	DateTimeOffset? LastExitAt,
	DateTimeOffset? LastHeartbeatAt,
	DateTimeOffset? LastHealthCheckAt,
	int ConsecutiveHealthFailures,
	int RestartCount,
	DateTimeOffset? NextRestartAt,
	string? LastError,
	IReadOnlyList<string> BootstrapOutput);

public record GetPluginRuntimeResponse(IReadOnlyList<PluginRuntimeSnapshotBody> Plugins);

public record PluginRuntimeActionResponse(bool Success, TransportError? Error);

[ApiController]
[Route("api/plugin-runtime")]
public class PluginRuntimeController : ControllerBase
{
	private readonly IPluginSupervisor _supervisor;

	public PluginRuntimeController(IPluginSupervisor supervisor)
	{
		_supervisor = supervisor;
	}

	[HttpGet]
	public GetPluginRuntimeResponse GetAll()
		=> new(_supervisor.Snapshot().Select(ToBody).ToList());

	[HttpPost("{pluginId}/start")]
	public async Task<PluginRuntimeActionResponse> Start(string pluginId, CancellationToken ct)
		=> ToResponse(await _supervisor.Start(pluginId, ct));

	[HttpPost("{pluginId}/stop")]
	public async Task<PluginRuntimeActionResponse> Stop(string pluginId, CancellationToken ct)
		=> ToResponse(await _supervisor.Stop(pluginId, PluginStopReason.UserRequested, ct));

	[HttpPost("{pluginId}/restart")]
	public async Task<PluginRuntimeActionResponse> Restart(string pluginId, CancellationToken ct)
		=> ToResponse(await _supervisor.Restart(pluginId, ct));

	private static PluginRuntimeActionResponse ToResponse(PluginSupervisorResult result)
		=> result.Success
			? new PluginRuntimeActionResponse(true, null)
			: new PluginRuntimeActionResponse(false,
				new TransportError
					{ Code = ToErrorCode(result.Error!.Value), Message = result.Message ?? string.Empty });

	private static string ToErrorCode(PluginSupervisorError error) => error switch
	{
		PluginSupervisorError.NotInstalled => "not_installed",
		PluginSupervisorError.ManifestInvalid => "manifest_invalid",
		PluginSupervisorError.NoEntrypointForRuntime => "no_entrypoint",
		PluginSupervisorError.AlreadyRunning => "already_running",
		PluginSupervisorError.NotRunning => "not_running",
		PluginSupervisorError.SelfRegistering => "self_registering",
		PluginSupervisorError.LaunchFailed => "launch_failed",
		PluginSupervisorError.IntegrityFailed => "integrity_failed",
		_ => "failed"
	};

	private static PluginRuntimeSnapshotBody ToBody(PluginRuntimeSnapshot snapshot) => new(snapshot.PluginId,
		snapshot.DisplayName,
		snapshot.Version,
		ToWireState(snapshot.State),
		ToWireHealth(snapshot.Health),
		snapshot.Managed,
		snapshot.ProcessId,
		snapshot.LaunchId,
		snapshot.StartedAt,
		snapshot.LastExitCode,
		ToWireStopReason(snapshot.LastStopReason),
		snapshot.LastExitAt,
		snapshot.LastHeartbeatAt,
		snapshot.LastHealthCheckAt,
		snapshot.ConsecutiveHealthFailures,
		snapshot.RestartCount,
		snapshot.NextRestartAt,
		snapshot.LastError,
		snapshot.BootstrapOutput);

	private static string ToWireState(PluginRuntimeState state) => state switch
	{
		PluginRuntimeState.Stopped => "stopped",
		PluginRuntimeState.Starting => "starting",
		PluginRuntimeState.Running => "running",
		PluginRuntimeState.Stopping => "stopping",
		PluginRuntimeState.Backoff => "backoff",
		PluginRuntimeState.Failed => "failed",
		_ => "stopped"
	};

	private static string ToWireHealth(PluginHealthState health) => health switch
	{
		PluginHealthState.Unknown => "unknown",
		PluginHealthState.Healthy => "healthy",
		PluginHealthState.Degraded => "degraded",
		PluginHealthState.Unhealthy => "unhealthy",
		PluginHealthState.Crashed => "crashed",
		PluginHealthState.Failed => "failed",
		_ => "unknown"
	};

	private static string ToWireStopReason(PluginStopReason reason) => reason switch
	{
		PluginStopReason.None => "none",
		PluginStopReason.UserRequested => "user_requested",
		PluginStopReason.HostShutdown => "host_shutdown",
		PluginStopReason.Update => "update",
		PluginStopReason.ManualRestart => "manual_restart",
		PluginStopReason.Crash => "crash",
		PluginStopReason.HealthFailure => "health_failure",
		PluginStopReason.LaunchFailure => "launch_failure",
		_ => "none"
	};
}
