namespace MacroDeckHost.Application.Notifications;

public interface IRestoreFailureNotifier
{
	Task NotifyIfFailed(CancellationToken cancellationToken = default);
}
