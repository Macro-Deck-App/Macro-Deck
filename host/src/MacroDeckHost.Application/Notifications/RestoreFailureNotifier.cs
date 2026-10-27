using MacroDeck.Localization;
using MacroDeckHost.Application.Backups;
using MacroDeckHost.Application.Lifecycle;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Notifications;

public sealed class RestoreFailureNotifier : IRestoreFailureNotifier
{
	public const string DedupeKey = "backup.restoreNotApplied";

	private readonly HostSession _session;
	private readonly IApplicationRestartService _restart;
	private readonly IUserNotificationStore _notifications;
	private readonly IAppPreferenceService _preferences;
	private readonly ILocalizationResolver _localization;

	public RestoreFailureNotifier(
		HostSession session,
		IApplicationRestartService restart,
		IUserNotificationStore notifications,
		IAppPreferenceService preferences,
		ILocalizationResolver localization)
	{
		_session = session;
		_restart = restart;
		_notifications = notifications;
		_preferences = preferences;
		_localization = localization;
	}

	public async Task NotifyIfFailed(CancellationToken cancellationToken = default)
	{
		if (_session.RestoreFailure == RestoreFailure.None)
		{
			return;
		}

		var culture = (await _preferences.GetLocalization()).Culture;
		var incomplete = _session.RestoreFailure == RestoreFailure.RollbackIncomplete;

		_notifications.Raise(new UserNotificationDraft
		{
			Severity = UserNotificationSeverity.Error,
			Kind = UserNotificationKind.General,
			Title = _localization.Resolve(AppStrings.Notifications.Backup.RestoreFailed(), culture),
			Message = _localization.Resolve(incomplete
					? AppStrings.Notifications.Backup.RestoreRollbackIncompleteMessage()
					: AppStrings.Notifications.Backup.RestoreNotCompletedMessage(),
				culture),
			Action = incomplete && _restart.Availability.Supported
				? new UserNotificationAction(UserNotificationActionKind.RestartApplication, null)
				: null,
			DedupeKey = DedupeKey
		});
	}
}
