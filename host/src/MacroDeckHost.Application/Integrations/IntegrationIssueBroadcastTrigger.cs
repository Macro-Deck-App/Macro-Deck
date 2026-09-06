namespace MacroDeckHost.Application.Integrations;

public sealed class IntegrationIssueBroadcastTrigger : IIntegrationIssueBroadcastTrigger, IDisposable
{
	private readonly SemaphoreSlim _pending = new(0, 1);

	public void RequestRefresh()
	{
		try
		{
			_pending.Release();
		}
		catch (SemaphoreFullException)
		{
		}
	}

	public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
		=> _pending.WaitAsync(timeout, cancellationToken);

	public void Dispose()
		=> _pending.Dispose();
}
