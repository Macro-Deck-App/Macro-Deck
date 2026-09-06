using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Lifecycle;
using MacroDeckHost.Application.Services;
using MacroDeck.Localization;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Notifications;

public sealed class NetworkRestartNotifier : INetworkRestartNotifier
{
	public const string DedupeKey = "network.restartRequired";

	private readonly IAppPreferenceService _preferences;
	private readonly IApplicationRestartService _restart;
	private readonly IUserNotificationStore _notifications;
	private readonly ILocalizationResolver _localization;

	public NetworkRestartNotifier(IAppPreferenceService preferences,
		IApplicationRestartService restart,
		IUserNotificationStore notifications,
		ILocalizationResolver localization)
	{
		_preferences = preferences;
		_restart = restart;
		_notifications = notifications;
		_localization = localization;
	}

	public async Task Sync(CancellationToken cancellationToken = default)
	{
		var settings = await _preferences.GetNetwork();

		var portChanged = !settings.OverriddenByEnvironment && settings.PublicPort != settings.ActivePublicPort;
		var restartRequired = portChanged || NetworkListenerIdentity.TlsOrCertificateDiffers(settings);

		if (!restartRequired)
		{
			_notifications.DismissByKey(DedupeKey);
			return;
		}

		var availability = _restart.Availability;
		var culture = (await _preferences.GetLocalization()).Culture;
		_notifications.Raise(new UserNotificationDraft
		{
			Severity = UserNotificationSeverity.Info,
			Kind = UserNotificationKind.General,
			Title = _localization.Resolve(AppStrings.Notifications.RestartToApplyNetwork(), culture) ??
				"Restart to apply the new network settings",
			Message = _localization.Resolve(availability.Supported
					? AppStrings.Notifications.NetworkSettingsPendingRestart()
					: AppStrings.Notifications.NetworkSettingsPendingManualRestart(),
				culture),
			Action = availability.Supported
				? new UserNotificationAction(UserNotificationActionKind.RestartApplication, null)
				: null,
			DedupeKey = DedupeKey
		});
	}
}
