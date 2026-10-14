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

	private readonly IUiTransportMessageHandler<MarkUserNotificationReadRequest, MarkUserNotificationReadResponse>
		_markRead;

	private readonly IUiTransportMessageHandler<MarkAllUserNotificationsReadRequest,
			MarkAllUserNotificationsReadResponse>
		_markAllRead;

	public NotificationsController(
		IUiTransportMessageHandler<GetUserNotificationsRequest, GetUserNotificationsResponse> getNotifications,
		IUiTransportMessageHandler<DismissUserNotificationRequest, DismissUserNotificationResponse> dismiss,
		IUiTransportMessageHandler<DismissAllUserNotificationsRequest, DismissAllUserNotificationsResponse> dismissAll,
		IUiTransportMessageHandler<MarkUserNotificationReadRequest, MarkUserNotificationReadResponse> markRead,
		IUiTransportMessageHandler<MarkAllUserNotificationsReadRequest, MarkAllUserNotificationsReadResponse>
			markAllRead)
	{
		_markRead = markRead;
		_markAllRead = markAllRead;
		_getNotifications = getNotifications;
		_dismiss = dismiss;
		_dismissAll = dismissAll;
	}

	[HttpGet]
	public Task<GetUserNotificationsResponse> GetNotifications(CancellationToken ct)
	{
		return _getNotifications.Handle(new GetUserNotificationsRequest(), ct).AsTask();
	}

	[HttpDelete("{id}")]
	public Task<DismissUserNotificationResponse> Dismiss(string id, CancellationToken ct)
	{
		return _dismiss.Handle(new DismissUserNotificationRequest { Id = id }, ct).AsTask();
	}

	[HttpDelete]
	public Task<DismissAllUserNotificationsResponse> DismissAll(CancellationToken ct)
	{
		return _dismissAll.Handle(new DismissAllUserNotificationsRequest(), ct).AsTask();
	}

	[HttpPut("{id}/read")]
	public Task<MarkUserNotificationReadResponse> MarkRead(string id, CancellationToken ct)
	{
		return _markRead.Handle(new MarkUserNotificationReadRequest { Id = id }, ct).AsTask();
	}

	[HttpPut("read")]
	public Task<MarkAllUserNotificationsReadResponse> MarkAllRead(CancellationToken ct)
	{
		return _markAllRead.Handle(new MarkAllUserNotificationsReadRequest(), ct).AsTask();
	}
}
