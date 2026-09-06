using MacroDeckHost.Application.Notifications;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Notifications;

public class GetUserNotificationsResponse
{
	public List<UserNotification> Notifications { get; set; } = [];
}
