using MacroDeckHost.Application.Notifications;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Notifications;

public class UserNotificationsChangedEvent
{
	public List<UserNotification> Notifications { get; set; } = [];
}
