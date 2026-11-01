namespace MacroDeckHost.Application.Widgets;

public sealed class WidgetLayoutGate : IDisposable
{
	private readonly SemaphoreSlim _semaphore = new(1, 1);

	// Not reentrant, and notification handlers call back into the gated services: dispose the lease
	// before publishing. Read the entities inside the lease, a stored one must not be a stale instance.
	public async Task<IDisposable> Enter(CancellationToken cancellationToken = default)
	{
		await _semaphore.WaitAsync(cancellationToken);
		return new Lease(_semaphore);
	}

	public void Dispose()
	{
		_semaphore.Dispose();
	}

	private sealed class Lease(SemaphoreSlim semaphore) : IDisposable
	{
		private int _released;

		public void Dispose()
		{
			if (Interlocked.Exchange(ref _released, 1) == 0)
			{
				semaphore.Release();
			}
		}
	}
}
