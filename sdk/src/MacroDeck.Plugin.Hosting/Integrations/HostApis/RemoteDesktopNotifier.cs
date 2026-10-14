using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Sdk.Notifications;
using ILogger = Serilog.ILogger;

namespace MacroDeck.Plugin.Hosting.Integrations.HostApis;

internal sealed class RemoteDesktopNotifier(IHostInvoker invoker, HostStateCache stateCache, ILogger logger)
	: IDesktopNotifier
{
	private readonly ILogger _logger = logger.ForContext<RemoteDesktopNotifier>();

	public bool IsSupported
		=> stateCache.Get<DesktopNotificationsStateDto>(Protocol.Callbacks.HostApis.DesktopNotifications)?.Supported
			== true;

	public async Task ShowAsync(string title, string message, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(title);
		ArgumentNullException.ThrowIfNull(message);
		cancellationToken.ThrowIfCancellationRequested();

		if (!IsSupported)
		{
			return;
		}

		try
		{
			await invoker.InvokeAsync(Protocol.Callbacks.HostApis.DesktopNotifications,
					HostOperations.DesktopNotifications.Show,
					new DesktopNotificationShowArguments { Title = title, Message = message },
					cancellationToken)
				.ConfigureAwait(false);
		}
		catch (HostInvocationException exception)
		{
			_logger.Debug("Macro Deck did not show a desktop notification: {Code}", exception.Code);
		}
	}
}
