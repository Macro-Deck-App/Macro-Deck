using MacroDeckHost.Application.Plugins;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class PluginAdbConsentDismissHandler(IPluginAdbConsentNotifier notifier)
	: INotificationHandler<AdbSettingsChangedNotification>
{
	public ValueTask Handle(AdbSettingsChangedNotification notification, CancellationToken cancellationToken)
	{
		if (notification.Settings is { Enabled: true, AllowPlugins: true })
		{
			notifier.DismissAll();
		}

		return ValueTask.CompletedTask;
	}
}
