namespace MacroDeckHost.Platform.Lock;

public interface ILockStateWatcher : IDisposable
{
	bool IsSupported { get; }

	IAsyncEnumerable<bool> WatchAsync(CancellationToken cancellationToken);
}
