using MacroDeck.Plugin.Hosting.Logging;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Sdk.Notifications;
using Serilog;

namespace MacroDeck.Plugin.Hosting.Integrations.HostApis;

/// <summary>
/// Proxies <see cref="IUserNotifier"/> over <c>host.invoke</c> against <see cref="HostApis.Notifications"/>.
/// Still goes through <c>host.invoke</c>/<c>host.result</c> rather than a fire-and-forget message type
/// of its own - uniformity with every other callback beats adding a fifth message type for two members.
/// The result is simply discarded (with an exception-swallowing continuation), which is what makes it
/// behave as fire-and-forget to <see cref="IUserNotifier"/>'s caller even though it is a real round
/// trip on the wire.
/// </summary>
internal sealed class RemoteUserNotifier(IHostInvoker invoker, ILogger logger) : IUserNotifier
{
	private readonly ILogger _logger = logger.ForContext<RemoteUserNotifier>();

	public void Notify(UserNotificationRequest notification)
	{
		if (notification is null || string.IsNullOrWhiteSpace(notification.Title))
		{
			return;
		}

		Fire(HostOperations.Notifications.Notify, notification, "notify");
	}

	public void Dismiss(string key)
	{
		if (string.IsNullOrWhiteSpace(key))
		{
			return;
		}

		Fire(HostOperations.Notifications.Dismiss, new NotificationsDismissArguments { Key = key }, "dismiss");
	}

	private void Fire(string operation, object arguments, string label)
	{
		_ = InvokeBestEffortAsync(operation, arguments, label);
	}

	private async Task InvokeBestEffortAsync(string operation, object arguments, string label)
	{
		try
		{
			await invoker.InvokeAsync(Protocol.Callbacks.HostApis.Notifications,
					operation,
					arguments,
					CancellationToken.None)
				.ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.HostCallbackFailed(Protocol.Callbacks.HostApis.Notifications, label, exception);
		}
	}
}
