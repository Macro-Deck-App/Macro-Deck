namespace MacroDeckHost.Application.Integrations;

public interface IIntegrationLifecycle
{
	Task ReinitializeAsync(string integrationId, CancellationToken cancellationToken = default);

	Task ShutdownAsync(string integrationId, CancellationToken cancellationToken = default);
}
