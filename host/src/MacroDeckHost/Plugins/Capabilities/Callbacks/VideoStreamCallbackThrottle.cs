namespace MacroDeckHost.Plugins.Capabilities.Callbacks;

public sealed class VideoStreamCallbackThrottle
{
	private readonly HostCallbackThrottle _bucket;

	public VideoStreamCallbackThrottle(TimeProvider timeProvider, int capacity = 64, double refillPerSecond = 32)
		=> _bucket = new HostCallbackThrottle(timeProvider, capacity, refillPerSecond);

	public bool TryConsume(string pluginId) => _bucket.TryConsume(pluginId);
}
