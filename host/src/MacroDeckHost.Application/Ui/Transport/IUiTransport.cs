namespace MacroDeckHost.Application.Ui.Transport;

public interface IUiTransport
{
	Task Send<T>(T message, CancellationToken cancellationToken = default)
		where T : class;

	Task SendToGroup<T>(string group, T message, CancellationToken cancellationToken = default)
		where T : class;

	Task SendToConnection<T>(string connectionId, T message, CancellationToken cancellationToken = default)
		where T : class;

	Task AddToGroup(string connectionId, string group, CancellationToken cancellationToken = default);

	Task RemoveFromGroup(string connectionId, string group, CancellationToken cancellationToken = default);
}
