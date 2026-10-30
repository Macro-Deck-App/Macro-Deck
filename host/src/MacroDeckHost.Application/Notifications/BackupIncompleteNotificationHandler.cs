using MacroDeck.Localization;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Localization;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Application.Notifications;

public sealed class BackupIncompleteNotificationHandler : INotificationHandler<BackupCompletedIncompleteNotification>
{
	public const string DedupeKey = "backup.incomplete";

	private readonly IUserNotificationStore _notifications;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ILocalizationResolver _localization;

	public BackupIncompleteNotificationHandler(
		IUserNotificationStore notifications,
		IServiceScopeFactory scopeFactory,
		ILocalizationResolver localization)
	{
		_notifications = notifications;
		_scopeFactory = scopeFactory;
		_localization = localization;
	}

	public async ValueTask Handle(
		BackupCompletedIncompleteNotification notification,
		CancellationToken cancellationToken)
	{
		var culture = await ActiveLocalization.Culture(_scopeFactory);

		_notifications.Raise(new UserNotificationDraft
		{
			Severity = UserNotificationSeverity.Warning,
			Kind = UserNotificationKind.General,
			Title = _localization.Resolve(AppStrings.Notifications.Backup.Incomplete(), culture),
			Message = _localization.Resolve(
				AppStrings.Notifications.Backup.IncompleteMessage(notification.SkippedFileCount),
				culture),
			DedupeKey = DedupeKey
		});
	}
}
