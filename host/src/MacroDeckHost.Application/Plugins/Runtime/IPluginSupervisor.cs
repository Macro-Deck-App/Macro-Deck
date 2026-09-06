using System.Diagnostics.CodeAnalysis;

namespace MacroDeckHost.Application.Plugins.Runtime;

public enum PluginSupervisorError
{
	NotInstalled,
	ManifestInvalid,
	NoEntrypointForRuntime,
	AlreadyRunning,
	NotRunning,
	LaunchFailed,
	SelfRegistering,
	Failed,

	DotnetRuntimeMissing,

	/// <summary>The installed plugin no longer verifies at the trust tier it was admitted at. Terminal for
	/// the process lifetime: no process is spawned, and the automatic reconcile loop does not retry it.
	/// Only an explicit Start/Restart re-evaluates it.</summary>
	IntegrityFailed
}

public sealed record PluginSupervisorResult(bool Success, PluginSupervisorError? Error = null, string? Message = null)
{
	public static PluginSupervisorResult Ok() => new(true);

	public static PluginSupervisorResult Fail(PluginSupervisorError error, string? message = null)
		=> new(false, error, message);
}

public interface IPluginSupervisor
{
	IReadOnlyList<PluginRuntimeSnapshot> Snapshot();

	Task<PluginSupervisorResult> Start(string pluginId, CancellationToken cancellationToken = default);

	[SuppressMessage("Naming",
		"CA1716:Identifiers should not match keywords",
		Justification = "'Stop' mirrors the REST verb and the state machine's own vocabulary; renaming it " +
			"for VB/other-language interop would be less readable than the interface it implements.")]
	Task<PluginSupervisorResult> Stop(string pluginId,
		PluginStopReason reason,
		CancellationToken cancellationToken = default);

	Task<PluginSupervisorResult> Restart(string pluginId, CancellationToken cancellationToken = default);

	Task StopAll(PluginStopReason reason, CancellationToken cancellationToken = default);

	Task Forget(string pluginId, CancellationToken cancellationToken = default);

	Task Reconcile(CancellationToken cancellationToken = default);
}
