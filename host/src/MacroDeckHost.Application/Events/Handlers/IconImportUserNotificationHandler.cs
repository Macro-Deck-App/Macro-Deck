using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class IconImportUserNotificationHandler : INotificationHandler<IconImportProgressNotification>
{
	private readonly IUserNotificationStore _store;
	private readonly IIconPackCache _iconPackCache;

	public IconImportUserNotificationHandler(IUserNotificationStore store, IIconPackCache iconPackCache)
	{
		_store = store;
		_iconPackCache = iconPackCache;
	}

	public ValueTask Handle(IconImportProgressNotification notification, CancellationToken cancellationToken)
	{
		var batch = notification.Batch;
		var isTerminal = batch.State is IconImportBatchState.Completed
			or IconImportBatchState.CompletedWithErrors
			or IconImportBatchState.Failed
			or IconImportBatchState.Cancelled;

		var dedupeKey = DedupeKey(batch);
		var draft = BuildDraft(batch, notification.Total, notification.Processed, notification.Failed, isTerminal);
		if (draft is null)
		{
			if (isTerminal)
			{
				_store.Retire(dedupeKey);
			}

			return ValueTask.CompletedTask;
		}

		if (isTerminal)
		{
			_store.Raise(draft);
		}
		else if (_store.RaiseIfAbsent(draft) is null)
		{
			_store.UpdateProgress(draft.DedupeKey!, draft.Progress!);
		}

		return ValueTask.CompletedTask;
	}

	private static string SkippedNote(IconImportBatchEntity batch)
		=> batch.Skipped switch
		{
			0 => "Nothing there could be imported as an icon.",
			1 => "1 file was already in this pack and was skipped.",
			_ => $"{batch.Skipped} files were already in this pack and were skipped."
		};

	private UserNotificationDraft? BuildDraft(IconImportBatchEntity batch,
		int? total,
		int processed,
		int failed,
		bool isTerminal)
	{
		var packName = _iconPackCache.GetPackById(batch.PackId)?.Name ?? batch.SourceName ?? "icon pack";

		(UserNotificationSeverity Severity, string Title, string? Message)? content = batch.State switch
		{
			IconImportBatchState.Completed when batch.Error is null =>
				processed == 0
					? (UserNotificationSeverity.Info, $"Nothing to import into {packName}", SkippedNote(batch))
					: null,
			IconImportBatchState.Completed =>
				(UserNotificationSeverity.Warning,
					$"Imported {processed} icons into {packName} with warnings",
					batch.Error),
			IconImportBatchState.CompletedWithErrors =>
				(UserNotificationSeverity.Warning, $"{processed} imported, {failed} failed into {packName}",
					batch.Error),
			IconImportBatchState.Failed =>
				(UserNotificationSeverity.Error, $"Failed to import icons into {packName}", batch.Error),
			IconImportBatchState.Cancelled => null,
			_ => (UserNotificationSeverity.Info, $"Importing icons into {packName}", null)
		};

		if (batch.Silent &&
			content?.Severity is not (UserNotificationSeverity.Warning or UserNotificationSeverity.Error))
		{
			return null;
		}

		if (content is null)
		{
			return null;
		}

		return new UserNotificationDraft
		{
			Severity = content.Value.Severity,
			Kind = UserNotificationKind.IconImport,
			Title = content.Value.Title,
			Message = content.Value.Message,
			Action = new UserNotificationAction(UserNotificationActionKind.OpenIconPacks, batch.PackId.ToString()),
			Progress = isTerminal ? null : new UserNotificationProgress(processed, total),
			CancelKey = isTerminal ? null : batch.Id.ToString(),
			DedupeKey = DedupeKey(batch)
		};
	}

	private static string DedupeKey(IconImportBatchEntity batch) => $"icon-import:{batch.Id}";
}
