using MacroDeck.Localization;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Localization;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Application.Events.Handlers;

/// <summary>Leaves a pending pairing request in the notification list as well as in the modal prompt: the
/// prompt is only visible while the window is, and a developer who was looking at their editor when the
/// request arrived would otherwise have nothing left to find.</summary>
public sealed class PluginPairingUserNotificationHandler : INotificationHandler<PluginPairingRequestedNotification>
{
	private readonly IUserNotificationStore _store;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ILocalizationResolver _localization;

	public PluginPairingUserNotificationHandler(
		IUserNotificationStore store,
		IServiceScopeFactory scopeFactory,
		ILocalizationResolver localization)
	{
		_store = store;
		_scopeFactory = scopeFactory;
		_localization = localization;
	}

	public async ValueTask Handle(PluginPairingRequestedNotification notification,
		CancellationToken cancellationToken)
	{
		var culture = await ActiveLocalization.Culture(_scopeFactory);

		_store.RaiseIfAbsent(new UserNotificationDraft
		{
			Severity = UserNotificationSeverity.Info,
			Kind = UserNotificationKind.Security,
			Title = _localization.Resolve(AppStrings.Notifications.PluginPairingRequested(), culture) ??
				notification.DisplayName,
			Message = _localization.Resolve(
				AppStrings.Notifications.PluginPairingRequestedMessage(name: notification.DisplayName),
				culture),
			SourceId = notification.PluginId,
			SourceName = notification.DisplayName,
			DedupeKey = $"plugin-pairing:{notification.PluginId}"
		});
	}
}
