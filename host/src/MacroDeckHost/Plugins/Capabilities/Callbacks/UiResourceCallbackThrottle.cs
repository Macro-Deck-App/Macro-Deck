using MacroDeck.Plugin.Protocol.Limits;

namespace MacroDeckHost.Plugins.Capabilities.Callbacks;

public sealed class UiResourceCallbackThrottle
{
	private readonly HostCallbackThrottle _bucket;

	public UiResourceCallbackThrottle(TimeProvider timeProvider)
		: this(timeProvider, 2 * ProtocolLimits.MaxUiResourcesPerPlugin, 64)
	{
	}

	public UiResourceCallbackThrottle(TimeProvider timeProvider, int capacity, double refillPerSecond)
		=> _bucket = new HostCallbackThrottle(timeProvider, capacity, refillPerSecond);

	public bool TryConsume(string pluginId) => _bucket.TryConsume(pluginId);
}
