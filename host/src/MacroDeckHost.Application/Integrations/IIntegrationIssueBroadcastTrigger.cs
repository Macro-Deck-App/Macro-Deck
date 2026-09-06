namespace MacroDeckHost.Application.Integrations;

public interface IIntegrationIssueBroadcastTrigger
{
	void RequestRefresh();

	Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken);
}
