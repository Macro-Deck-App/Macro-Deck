namespace MacroDeckHost.Application.Notifications;

public interface IPublicListenerUnavailableNotifier
{
	Task NotifyIfUnavailable(CancellationToken cancellationToken = default);
}
