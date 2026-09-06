using MacroDeck.Localization;
using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Lifecycle;
using MacroDeckHost.Application.Network.Tls;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Notifications;

public sealed class PublicListenerUnavailableNotifier : IPublicListenerUnavailableNotifier
{
	public const string DedupeKey = "network.publicListenerUnavailable";

	public const string TlsDedupeKey = "network.publicTlsUnavailable";

	private readonly IHostListenerState _listenerState;
	private readonly IApplicationRestartService _restart;
	private readonly IUserNotificationStore _notifications;
	private readonly IAppPreferenceService _preferences;
	private readonly ILocalizationResolver _localization;

	public PublicListenerUnavailableNotifier(IHostListenerState listenerState,
		IApplicationRestartService restart,
		IUserNotificationStore notifications,
		IAppPreferenceService preferences,
		ILocalizationResolver localization)
	{
		_listenerState = listenerState;
		_restart = restart;
		_notifications = notifications;
		_preferences = preferences;
		_localization = localization;
	}

	public async Task NotifyIfUnavailable(CancellationToken cancellationToken = default)
	{
		if (!_listenerState.PublicListenerAvailable)
		{
			NotifyNothingIsReachable((await _preferences.GetLocalization()).Culture);
			return;
		}

		if (HttpsWasRequestedButIsNotServing())
		{
			NotifyHttpsIsNotServing((await _preferences.GetLocalization()).Culture);
		}
	}

	private bool HttpsWasRequestedButIsNotServing()
		=> _listenerState.PublicEndpoints.HttpsPort is null &&
			(_listenerState.TlsFailure != PublicTlsFailure.None ||
				_listenerState.TlsRejection != PublicTlsRejection.None ||
				_listenerState.PublicEndpoints.TlsMode != PublicTlsMode.Disabled);

	private void NotifyNothingIsReachable(string? culture)
	{
		var offerRestart = !_listenerState.PublicPortOverriddenByEnvironment && _restart.Availability.Supported;

		var tlsIsTheCause = _listenerState.TlsFailure != PublicTlsFailure.None;

		_notifications.Raise(new UserNotificationDraft
		{
			Severity = UserNotificationSeverity.Warning,
			Kind = UserNotificationKind.General,
			Title = Resolve(AppStrings.Notifications.NetworkUnreachable(), culture),
			Message = tlsIsTheCause
				? Resolve(AppStrings.Notifications.NetworkUnreachableTlsMessage(), culture) +
				" " +
				Resolve(CertificateAdvice(_listenerState.TlsFailure), culture)
				: Resolve(_listenerState.PublicPortOverriddenByEnvironment
						? AppStrings.Notifications.NetworkUnreachableEnvironmentMessage()
						: AppStrings.Notifications.NetworkUnreachablePortMessage(),
					culture),
			Action = offerRestart
				? new UserNotificationAction(UserNotificationActionKind.RestartApplication, null)
				: null,
			DedupeKey = DedupeKey
		});
	}

	private void NotifyHttpsIsNotServing(string? culture)
	{
		_notifications.Raise(new UserNotificationDraft
		{
			Severity = UserNotificationSeverity.Warning,
			Kind = UserNotificationKind.General,
			Title = Resolve(AppStrings.Notifications.HttpsNotRunning(), culture),
			Message = _listenerState.TlsFailure != PublicTlsFailure.None
				? Resolve(AppStrings.Notifications.HttpsNotRunningCertificateMessage(), culture) +
				" " +
				Resolve(CertificateAdvice(_listenerState.TlsFailure), culture)
				: Resolve(AppStrings.Notifications.HttpsNotRunningMessage(), culture),
			Action = _restart.Availability.Supported
				? new UserNotificationAction(UserNotificationActionKind.RestartApplication, null)
				: null,
			DedupeKey = TlsDedupeKey
		});
	}

	private string Resolve(LocalizedString value, string? culture) => _localization.Resolve(value, culture);

	private static LocalizedString CertificateAdvice(PublicTlsFailure failure)
		=> failure switch
		{
			PublicTlsFailure.KeyUnreadable => AppStrings.Notifications.TlsCertificateKeyUnreadableAdvice(),
			PublicTlsFailure.CertificateInvalid => AppStrings.Notifications.TlsCertificateInvalidAdvice(),
			_ => AppStrings.Notifications.TlsCertificateMissingAdvice()
		};
}
