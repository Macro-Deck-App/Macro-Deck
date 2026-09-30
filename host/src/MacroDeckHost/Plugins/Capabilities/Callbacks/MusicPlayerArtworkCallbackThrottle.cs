namespace MacroDeckHost.Plugins.Capabilities.Callbacks;

public sealed class MusicPlayerArtworkCallbackThrottle
{
	private readonly HostCallbackThrottle _bucket;

	public MusicPlayerArtworkCallbackThrottle(TimeProvider timeProvider)
		: this(timeProvider, 16, 4)
	{
	}

	public MusicPlayerArtworkCallbackThrottle(TimeProvider timeProvider, int capacity, double refillPerSecond)
		=> _bucket = new HostCallbackThrottle(timeProvider, capacity, refillPerSecond);

	public bool TryConsume(string pluginId) => _bucket.TryConsume(pluginId);
}
