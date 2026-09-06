using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Notifications;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetUserNotificationsRequestMessageHandler
	: IUiTransportMessageHandler<GetUserNotificationsRequest, GetUserNotificationsResponse>
{
	private readonly IUserNotificationStore _store;

	public GetUserNotificationsRequestMessageHandler(IUserNotificationStore store)
	{
		_store = store;
	}

	public ValueTask<GetUserNotificationsResponse> Handle(
		GetUserNotificationsRequest request,
		CancellationToken cancellationToken)
	{
		var response = new GetUserNotificationsResponse { Notifications = _store.Snapshot().ToList() };
		return ValueTask.FromResult(response);
	}
}

public class DismissUserNotificationRequestMessageHandler
	: IUiTransportMessageHandler<DismissUserNotificationRequest, DismissUserNotificationResponse>
{
	private readonly IUserNotificationStore _store;

	public DismissUserNotificationRequestMessageHandler(IUserNotificationStore store)
	{
		_store = store;
	}

	public ValueTask<DismissUserNotificationResponse> Handle(
		DismissUserNotificationRequest request,
		CancellationToken cancellationToken)
	{
		var response = new DismissUserNotificationResponse { Success = _store.Dismiss(request.Id) };
		return ValueTask.FromResult(response);
	}
}

public class DismissAllUserNotificationsRequestMessageHandler
	: IUiTransportMessageHandler<DismissAllUserNotificationsRequest, DismissAllUserNotificationsResponse>
{
	private readonly IUserNotificationStore _store;

	public DismissAllUserNotificationsRequestMessageHandler(IUserNotificationStore store)
	{
		_store = store;
	}

	public ValueTask<DismissAllUserNotificationsResponse> Handle(
		DismissAllUserNotificationsRequest request,
		CancellationToken cancellationToken)
	{
		var response = new DismissAllUserNotificationsResponse { Success = _store.DismissAll() };
		return ValueTask.FromResult(response);
	}
}
