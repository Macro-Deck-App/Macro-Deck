using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Runtime;

namespace MacroDeckHost.Tests.UnitTests.Plugins.Installation;

/// <summary>An <see cref="IPluginSupervisor"/> that fakes the process lifecycle but not the shape of
/// <see cref="Snapshot"/>: like the real supervisor, it composes the snapshot from the installation
/// catalog and the session registry, so a caller that filters on <c>Managed</c>, or that expects an
/// installed-but-stopped plugin to be listed, sees here what it will see in production.
///
/// Not modelled: process ids, launch ids, timestamps, restart counts, and the metadata the real
/// supervisor seeds from the manifest at launch. A test that needs those needs the real supervisor.</summary>
internal sealed class FakeInstallSupervisor : IPluginSupervisor
{
	private readonly IPluginInstallationCatalog _catalog;
	private readonly IPluginSessionRegistry _sessions;

	private readonly Dictionary<string, (PluginRuntimeState State, PluginHealthState Health)> _runtime =
		new(StringComparer.Ordinal);

	public FakeInstallSupervisor(IPluginInstallationCatalog catalog, IPluginSessionRegistry sessions)
	{
		_catalog = catalog;
		_sessions = sessions;
	}

	public HashSet<string> UnhealthyPlugins { get; } = new(StringComparer.Ordinal);

	public List<(string PluginId, PluginStopReason Reason)> Stops { get; } = [];

	public List<string> Starts { get; } = [];

	public Dictionary<string, bool> DesiredStartState { get; } = new(StringComparer.Ordinal);

	public IReadOnlyList<PluginRuntimeSnapshot> Snapshot()
	{
		var installed = _catalog.Discover().Where(plugin => plugin.Versions.Count > 0).ToList();
		var installedIds = installed.Select(plugin => plugin.PluginId).ToHashSet(StringComparer.Ordinal);

		var result = installed.Select(plugin => ManagedSnapshot(plugin.PluginId)).ToList();

		foreach (var session in _sessions.Snapshot())
		{
			if (session.Origin == PluginSessionOrigin.Managed || installedIds.Contains(session.PluginId))
			{
				continue;
			}

			var connected = session.State == PluginSessionState.Connected;
			result.Add(new PluginRuntimeSnapshot
			{
				PluginId = session.PluginId,
				DisplayName = PluginDisplayNameResolver.Resolve(session, manifestName: null, session.PluginId),
				Version = PluginVersionResolver.Resolve(installedVersion: null, session.DeclaredVersion),
				State = connected ? PluginRuntimeState.Running : PluginRuntimeState.Stopped,
				Health = connected ? PluginHealthState.Healthy : PluginHealthState.Unknown,
				Managed = false,
				LastHeartbeatAt = session.LastInboundAt
			});
		}

		return result;
	}

	public Task<PluginSupervisorResult> Start(string pluginId, CancellationToken cancellationToken = default)
	{
		Starts.Add(pluginId);
		DesiredStartState[pluginId] = true;

		_runtime[pluginId] = UnhealthyPlugins.Contains(pluginId)
			? (PluginRuntimeState.Failed, PluginHealthState.Crashed)
			: (PluginRuntimeState.Running, PluginHealthState.Healthy);

		return Task.FromResult(PluginSupervisorResult.Ok());
	}

	public Task<PluginSupervisorResult> Stop(string pluginId,
		PluginStopReason reason,
		CancellationToken cancellationToken = default)
	{
		Stops.Add((pluginId, reason));
		_runtime.Remove(pluginId);

		if (reason == PluginStopReason.UserRequested && DesiredStartState.ContainsKey(pluginId))
		{
			DesiredStartState[pluginId] = false;
		}

		return Task.FromResult(PluginSupervisorResult.Ok());
	}

	public Task Forget(string pluginId, CancellationToken cancellationToken = default)
	{
		_runtime.Remove(pluginId);
		DesiredStartState.Remove(pluginId);
		return Task.CompletedTask;
	}

	public Task<PluginSupervisorResult> Restart(string pluginId, CancellationToken cancellationToken = default)
		=> Start(pluginId, cancellationToken);

	public Task StopAll(PluginStopReason reason, CancellationToken cancellationToken = default)
	{
		_runtime.Clear();
		return Task.CompletedTask;
	}

	public Task Reconcile(CancellationToken cancellationToken = default) => Task.CompletedTask;

	public bool IsRunning(string pluginId)
		=> Snapshot().Any(snapshot => string.Equals(snapshot.PluginId, pluginId, StringComparison.Ordinal) &&
			snapshot.State == PluginRuntimeState.Running);

	private PluginRuntimeSnapshot ManagedSnapshot(string pluginId)
	{
		var (state, health) = _runtime.TryGetValue(pluginId, out var running)
			? running
			: (PluginRuntimeState.Stopped, PluginHealthState.Unknown);

		return new PluginRuntimeSnapshot
		{
			PluginId = pluginId,
			DisplayName = pluginId,
			Version = PluginRuntimeSnapshot.UnknownVersion,
			State = state,
			Health = health,
			Managed = true
		};
	}
}
