using MacroDeck.Localization;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Application.Notifications;

public sealed class PersistenceRecoveryNotifier : IPersistenceRecoveryReporter
{
	public const string RecoveredDedupeKey = "persistence.filesRecovered";

	public const string UnrecoverableDedupeKey = "persistence.filesUnreadable";

	private readonly object _lock = new();
	private readonly HashSet<string> _recovered = new(StringComparer.Ordinal);
	private readonly HashSet<string> _unrecoverable = new(StringComparer.Ordinal);
	private readonly IUserNotificationStore _notifications;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ILocalizationResolver _localization;

	public PersistenceRecoveryNotifier(IUserNotificationStore notifications,
		IServiceScopeFactory scopeFactory,
		ILocalizationResolver localization)
	{
		_notifications = notifications;
		_scopeFactory = scopeFactory;
		_localization = localization;
	}

	public void ReportRecovered(PersistenceRecovery recovery)
	{
		int count;
		bool anyCorruptPreserved;
		lock (_lock)
		{
			// Several stores re-read the same document on every save, so a report is only news the
			// first time a given path is named; counting reports would inflate the same loss forever.
			if (!_recovered.Add(recovery.Path))
			{
				return;
			}

			count = _recovered.Count;
			anyCorruptPreserved = recovery.PreservedCorruptPath is not null || count > 1;
		}

		var culture = ActiveCulture();
		var message = _localization.Resolve(AppStrings.Notifications.FilesRecoveredMessage(count: count), culture);
		if (anyCorruptPreserved)
		{
			message += " " +
				_localization.Resolve(AppStrings.Notifications.FilesRecoveredCorruptCopiesKept(), culture);
		}

		_notifications.Raise(new UserNotificationDraft
		{
			Severity = UserNotificationSeverity.Warning,
			Kind = UserNotificationKind.General,
			Title = _localization.Resolve(AppStrings.Notifications.FilesRecovered(), culture),
			Message = message,
			DedupeKey = RecoveredDedupeKey,
		});
	}

	public void ReportUnrecoverable(PersistenceLoss loss)
	{
		int count;
		lock (_lock)
		{
			if (!_unrecoverable.Add(loss.Path))
			{
				return;
			}

			count = _unrecoverable.Count;
		}

		var culture = ActiveCulture();

		_notifications.Raise(new UserNotificationDraft
		{
			Severity = UserNotificationSeverity.Error,
			Kind = UserNotificationKind.Error,
			Title = _localization.Resolve(AppStrings.Notifications.FilesUnreadable(), culture),
			Message = _localization.Resolve(AppStrings.Notifications.FilesUnreadableMessage(count: count), culture),
			DedupeKey = UnrecoverableDedupeKey,
		});
	}

	// The reporter contract is synchronous - the JSON stores report from inside their own reads - while
	// only an asynchronous preference read can answer which language the text has to be in, so this
	// blocks. It runs once per newly damaged file and never on a hot path. A damaged file can also be
	// found before the preference database is usable, so a failure falls back to the default culture
	// instead of costing the notification.
	private string? ActiveCulture()
	{
		try
		{
			return ActiveLocalization.Culture(_scopeFactory).GetAwaiter().GetResult();
		}
		catch (Exception)
		{
			return null;
		}
	}
}
