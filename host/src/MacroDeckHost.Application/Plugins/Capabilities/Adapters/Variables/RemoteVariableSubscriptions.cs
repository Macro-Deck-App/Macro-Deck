using System.Collections.Concurrent;
using MacroDeck.Plugin.Protocol.Limits;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Application.Plugins.Capabilities.Adapters.Variables;

// The catalog working set most recently declared to each plugin, keyed by plugin id. Written by
// RemotePluginIntegration's SubscribeAsync forward every time the host tells a plugin's provider what to
// watch, and read by PluginCallbackRouter to police the variable-values "value" callback: a plugin must
// not be able to push a value for a resource id the host is not currently watching from it.
//
// Shaped like Devices.RemoteDeviceSessionRegistry - a small ownership record the wire layer consults, kept
// independent of the higher-level subscription bookkeeping in MacroDeckHost.Application.Variables so the
// host-callback boundary never has to reach into that subsystem to answer a question this simple.
public sealed class RemoteVariableSubscriptions
{
	private readonly ConcurrentDictionary<string, IReadOnlySet<string>> _byPlugin = new(StringComparer.Ordinal);
	private readonly ILogger _logger;

	public RemoteVariableSubscriptions(ILogger logger)
	{
		_logger = logger.ForContext<RemoteVariableSubscriptions>();
	}

	// Replaces the working set for pluginId, never increments it - the same "authoritative working set"
	// contract IVariableProvider.SubscribeAsync documents. Clamped to
	// ProtocolLimits.MaxVariableSubscriptions so this record of what a plugin is watching stays consistent
	// with the identical clamp its own SDK-side VariableSubscriptions applies - the host is otherwise never
	// told that the excess ids were dropped.
	public void Replace(string pluginId, IReadOnlyCollection<string> ids)
	{
		if (ids.Count > ProtocolLimits.MaxVariableSubscriptions)
		{
			_logger.Warning("Plugin '{PluginId}' was asked to watch {Requested} variable ids, more than the " +
				"{Max} a single plugin may watch at once; the excess ids were dropped",
				pluginId,
				ids.Count,
				ProtocolLimits.MaxVariableSubscriptions);
		}

		_byPlugin[pluginId] = new HashSet<string>(ids.Take(ProtocolLimits.MaxVariableSubscriptions),
			StringComparer.Ordinal);
	}

	/// <summary>Whether <paramref name="localResourceId"/> is part of the working set most recently
	/// declared to <paramref name="pluginId"/>'s provider.</summary>
	public bool IsSubscribed(string pluginId, string localResourceId)
		=> _byPlugin.TryGetValue(pluginId, out var ids) && ids.Contains(localResourceId);
}
