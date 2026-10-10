using MacroDeck.Sdk.Notifications;

namespace MacroDeckHost.Integrations.System.Actions;

internal sealed class NotificationServiceAccessor
{
	public INotificationService? Current { get; set; }
}
