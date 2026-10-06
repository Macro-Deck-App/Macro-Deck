namespace MacroDeckHost.Application.Calendar;

public interface ICalendarSyncSignal
{
	void RequestSync();

	Task WaitAsync(TimeSpan timeout, TimeProvider time, CancellationToken cancellationToken);
}

public sealed class CalendarSyncSignal : ICalendarSyncSignal, IDisposable
{
	private readonly SemaphoreSlim _pending = new(0, 1);

	public void RequestSync()
	{
		try
		{
			_pending.Release();
		}
		catch (SemaphoreFullException)
		{
		}
	}

	public async Task WaitAsync(TimeSpan timeout, TimeProvider time, CancellationToken cancellationToken)
	{
		using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		var delay = Task.Delay(timeout, time, linked.Token);
		var signalled = _pending.WaitAsync(linked.Token);

		await Task.WhenAny(delay, signalled).ConfigureAwait(false);
		await linked.CancelAsync().ConfigureAwait(false);
		cancellationToken.ThrowIfCancellationRequested();
	}

	public void Dispose() => _pending.Dispose();
}
