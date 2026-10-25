using MacroDeck.Localization;
using MacroDeckHost.Application.Backups;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Localization;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class BackupUserNotificationHandler : INotificationHandler<BackupOperationProgressNotification>
{
	public const string CancelKeyPrefix = "backup:";

	private readonly IUserNotificationStore _store;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ILocalizationResolver _localization;

	public BackupUserNotificationHandler(
		IUserNotificationStore store,
		IServiceScopeFactory scopeFactory,
		ILocalizationResolver localization)
	{
		_store = store;
		_scopeFactory = scopeFactory;
		_localization = localization;
	}

	public async ValueTask Handle(BackupOperationProgressNotification notification, CancellationToken cancellationToken)
	{
		var status = notification.Status;
		if (status.OperationId is not { } operationId
			|| status.Kind is not (BackupOperationKind.Create
				or BackupOperationKind.Restore))
		{
			return;
		}

		var safetyBackup = status.Kind == BackupOperationKind.Create && status.Trigger == BackupTrigger.BeforeRestore;
		var restoring = status.Kind == BackupOperationKind.Restore || safetyBackup;

		var key = CancelKeyPrefix + operationId.ToString("D");
		if (status.Stage is not (BackupOperationStage.Completed or BackupOperationStage.Failed))
		{
			await ShowRunning(status, key, safetyBackup, restoring);
			return;
		}

		// A restore reports its own end after its safety backup, so the safety backup's end is not shown.
		if (safetyBackup)
		{
			return;
		}

		if (status.Stage == BackupOperationStage.Completed && restoring
			|| status.Error == BackupError.Cancelled)
		{
			_store.Retire(key);
			return;
		}

		var culture = await ActiveLocalization.Culture(_scopeFactory);
		_store.Raise(status.Stage == BackupOperationStage.Completed
			? new UserNotificationDraft
			{
				Severity = UserNotificationSeverity.Success,
				Kind = UserNotificationKind.General,
				Title = Resolve(AppStrings.Settings.Backups.CompletedToast(), culture),
				DedupeKey = key
			}
			: new UserNotificationDraft
			{
				Severity = UserNotificationSeverity.Error,
				Kind = UserNotificationKind.General,
				Title = Resolve(restoring
						? AppStrings.Notifications.Backup.RestoreFailed()
						: AppStrings.Settings.Backups.FailedHeading(),
					culture),
				Message = status.ErrorMessage,
				DedupeKey = key
			});
	}

	private async Task ShowRunning(BackupOperationStatus status, string key, bool safetyBackup, bool restoring)
	{
		var progress = new UserNotificationProgress(status.PercentComplete ?? 0,
			status.PercentComplete is null ? null : 100);
		var running = _store.Snapshot()
			.FirstOrDefault(entry => entry.Progress is not null
				&& entry.CancelKey?.StartsWith(CancelKeyPrefix, StringComparison.Ordinal) == true);

		// The safety backup runs inside a restore and moves the restore's entry rather than adding its own.
		if (running?.CancelKey is { } runningKey && (runningKey == key || safetyBackup))
		{
			_store.UpdateProgress(runningKey, progress);
			return;
		}

		var culture = await ActiveLocalization.Culture(_scopeFactory);
		_store.RaiseIfAbsent(new UserNotificationDraft
		{
			Severity = UserNotificationSeverity.Info,
			Kind = UserNotificationKind.General,
			Title = Resolve(restoring
					? AppStrings.Backup.Progress.TitleRestoring()
					: AppStrings.Backup.Progress.TitleCreating(),
				culture),
			Progress = progress,
			CancelKey = key,
			DedupeKey = key
		});
	}

	private string Resolve(LocalizedText text, string? culture)
	{
		return _localization.Resolve(text, culture) ?? string.Empty;
	}
}
