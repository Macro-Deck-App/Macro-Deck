using MacroDeck.Sdk.Notifications;

namespace MacroDeckHost.Integrations.System.Actions;

internal sealed class DesktopNotifierAccessor
{
	public IDesktopNotifier? Current { get; set; }
}
