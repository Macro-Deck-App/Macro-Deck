using System.Runtime.CompilerServices;

namespace MacroDeckHost.Integrations.System.Lock;

public sealed class NullLockStateWatcher : ILockStateWatcher
{
	public bool IsSupported => false;

	public async IAsyncEnumerable<bool> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		await Task.CompletedTask;
		yield break;
	}

	public void Dispose()
	{
	}
}
