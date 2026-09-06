using System.Collections.Concurrent;

namespace MacroDeckHost.Application.Widgets;

public interface IWidgetDataWriteLock
{
	Task<IDisposable> AcquireAsync(Guid widgetId, CancellationToken cancellationToken = default);
}

public sealed class WidgetDataWriteLock : IWidgetDataWriteLock
{
	private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _gates = new();

	public async Task<IDisposable> AcquireAsync(Guid widgetId, CancellationToken cancellationToken = default)
	{
		var gate = _gates.GetOrAdd(widgetId, static _ => new SemaphoreSlim(1, 1));
		await gate.WaitAsync(cancellationToken);
		return new Release(gate);
	}

	private sealed class Release : IDisposable
	{
		private readonly SemaphoreSlim _gate;
		private int _released;

		public Release(SemaphoreSlim gate) => _gate = gate;

		public void Dispose()
		{
			if (Interlocked.Exchange(ref _released, 1) == 0)
			{
				_gate.Release();
			}
		}
	}
}
