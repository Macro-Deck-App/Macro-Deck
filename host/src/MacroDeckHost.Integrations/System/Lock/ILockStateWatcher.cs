namespace MacroDeckHost.Integrations.System.Lock;

public interface ILockStateWatcher : IDisposable
{
	bool IsSupported { get; }

	IAsyncEnumerable<bool> WatchAsync(CancellationToken cancellationToken);
}
