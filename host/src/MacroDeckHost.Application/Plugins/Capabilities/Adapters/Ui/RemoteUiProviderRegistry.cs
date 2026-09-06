using System.Collections.Concurrent;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeckHost.Application.Ui.Sessions;

namespace MacroDeckHost.Application.Plugins.Capabilities.Adapters.Ui;

public sealed class RemoteUiProviderRegistry
{
	private readonly IRemotePluginSnapshotStore _snapshots;
	private readonly IPluginCapabilityInvoker _invoker;

	private readonly ConcurrentDictionary<string, RemoteUiSessionProvider> _adapters = new(StringComparer.Ordinal);

	public RemoteUiProviderRegistry(IRemotePluginSnapshotStore snapshots, IPluginCapabilityInvoker invoker)
	{
		_snapshots = snapshots;
		_invoker = invoker;
	}

	public IUiSessionProvider? Resolve(string providerId)
	{
		if (!_snapshots.Has(providerId) ||
			!_snapshots.GetSnapshot(providerId).AcceptedKinds.Contains(CapabilityKinds.Ui, StringComparer.Ordinal))
		{
			return null;
		}

		return _adapters.GetOrAdd(providerId,
			static (id, invoker) => new RemoteUiSessionProvider(id, invoker),
			_invoker);
	}
}
