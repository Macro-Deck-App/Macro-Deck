using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Notifications;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Controllers;

[ApiController]
[Route("api/notifications")]
public class NotificationsController : ControllerBase
{
	private readonly IUiTransportMessageHandler<GetUserNotificationsRequest, GetUserNotificationsResponse>
		_getNotifications;

	private readonly IUiTransportMessageHandler<DismissUserNotificationRequest, DismissUserNotificationResponse>
		_dismiss;

	private readonly IUiTransportMessageHandler<DismissAllUserNotificationsRequest, DismissAllUserNotificationsResponse>
		_dismissAll;

	public NotificationsController(
		IUiTransportMessageHandler<GetUserNotificationsRequest, GetUserNotificationsResponse> getNotifications,
		IUiTransportMessageHandler<DismissUserNotificationRequest, DismissUserNotificationResponse> dismiss,
		IUiTransportMessageHandler<DismissAllUserNotificationsRequest, DismissAllUserNotificationsResponse> dismissAll)
	{
		_getNotifications = getNotifications;
		_dismiss = dismiss;
		_dismissAll = dismissAll;
	}

	[HttpGet]
	public Task<GetUserNotificationsResponse> GetNotifications(CancellationToken ct)
		=> _getNotifications.Handle(new GetUserNotificationsRequest(), ct).AsTask();

	[HttpDelete("{id}")]
	public Task<DismissUserNotificationResponse> Dismiss(string id, CancellationToken ct)
		=> _dismiss.Handle(new DismissUserNotificationRequest { Id = id }, ct).AsTask();

	[HttpDelete]
	public Task<DismissAllUserNotificationsResponse> DismissAll(CancellationToken ct)
		=> _dismissAll.Handle(new DismissAllUserNotificationsRequest(), ct).AsTask();
}
