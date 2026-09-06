using MacroDeck.Localization;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Integrations.System.Notifications;
using MacroDeckHost.Localization;
using Mediator;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Plugins.Pairing;

/// <summary>
/// Raises an OS notification for a new pairing request, so a developer whose Macro Deck window is behind
/// their editor still learns that something is waiting for them.
/// <para>
/// The notification is dispatched on a background task and every failure is swallowed. Mediator awaits
/// notification handlers in sequence inside the pairing HTTP request, and the platform implementations
/// start a helper process (<c>osascript</c>, <c>notify-send</c>) and throw when it cannot be started - so
/// awaiting this inline would let a missing helper fail a pairing request that already succeeded, and
/// skip the handler that pushes the prompt to the desktop UI.
/// </para>
/// </summary>
public sealed class PluginPairingSystemNotificationHandler : INotificationHandler<PluginPairingRequestedNotification>
{
	private readonly INotificationService _notifications;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ILocalizationResolver _localization;
	private readonly ILogger _logger;

	public PluginPairingSystemNotificationHandler(
		INotificationService notifications,
		IServiceScopeFactory scopeFactory,
		ILocalizationResolver localization,
		ILogger logger)
	{
		_notifications = notifications;
		_scopeFactory = scopeFactory;
		_localization = localization;
		_logger = logger;
	}

	public async ValueTask Handle(PluginPairingRequestedNotification notification,
		CancellationToken cancellationToken)
	{
		if (!_notifications.IsSupported)
		{
			return;
		}

		var culture = await ActiveLocalization.Culture(_scopeFactory);
		var title = _localization.Resolve(AppStrings.Notifications.PluginPairingRequested(), culture) ??
			notification.DisplayName;
		var message = _localization.Resolve(
				AppStrings.Notifications.PluginPairingRequestedMessage(name: notification.DisplayName),
				culture) ??
			notification.DisplayName;

		_ = Task.Run(async () =>
			{
				try
				{
					await _notifications.ShowAsync(title, message, CancellationToken.None);
				}
				catch (Exception exception)
				{
					_logger.Debug(exception, "Showing the pairing system notification failed.");
				}
			},
			CancellationToken.None);
	}
}
