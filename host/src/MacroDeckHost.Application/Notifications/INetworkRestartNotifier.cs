namespace MacroDeckHost.Application.Notifications;

public interface INetworkRestartNotifier
{
	Task Sync(CancellationToken cancellationToken = default);
}
