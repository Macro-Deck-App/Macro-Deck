using System.Globalization;
using MacroDeck.Localization;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Localization;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class IconImportUserNotificationHandler : INotificationHandler<IconImportProgressNotification>
{
	private readonly IUserNotificationStore _store;
	private readonly IIconPackCache _iconPackCache;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ILocalizationResolver _localization;

	public IconImportUserNotificationHandler(IUserNotificationStore store,
		IIconPackCache iconPackCache,
		IServiceScopeFactory scopeFactory,
		ILocalizationResolver localization)
	{
		_store = store;
		_iconPackCache = iconPackCache;
		_scopeFactory = scopeFactory;
		_localization = localization;
	}

	public async ValueTask Handle(IconImportProgressNotification notification, CancellationToken cancellationToken)
	{
		var batch = notification.Batch;
		var isTerminal = batch.State is IconImportBatchState.Completed
			or IconImportBatchState.CompletedWithErrors
			or IconImportBatchState.Failed
			or IconImportBatchState.Cancelled;

		var dedupeKey = DedupeKey(batch);
		var draft = await BuildDraft(batch, notification.Total, notification.Processed, notification.Failed, isTerminal);
		if (draft is null)
		{
			if (isTerminal)
			{
				_store.Retire(dedupeKey);
			}

			return;
		}

		if (isTerminal)
		{
			_store.Raise(draft);
		}
		else if (_store.RaiseIfAbsent(draft) is null)
		{
			_store.UpdateProgress(draft.DedupeKey!, draft.Progress!);
		}
	}

	private static LocalizedText SkippedNote(IconImportBatchEntity batch)
		=> batch.Skipped == 0
			? AppStrings.Notifications.IconImport.NothingImportable()
			: AppStrings.Notifications.IconImport.Skipped(count: batch.Skipped);

	private async Task<UserNotificationDraft?> BuildDraft(IconImportBatchEntity batch,
		int? total,
		int processed,
		int failed,
		bool isTerminal)
	{
		if (batch.State is IconImportBatchState.Cancelled ||
			(batch.State is IconImportBatchState.Completed && batch.Error is null && processed > 0))
		{
			return null;
		}

		var culture = await ActiveLocalization.Culture(_scopeFactory);
		var pack = _iconPackCache.GetPackById(batch.PackId)?.Name ??
			batch.SourceName ??
			Resolve(AppStrings.Notifications.IconImport.DefaultPackName(), culture);

		(UserNotificationSeverity Severity, LocalizedText Title, string? Message) content = batch.State switch
		{
			IconImportBatchState.Completed when batch.Error is null =>
				(UserNotificationSeverity.Info,
					AppStrings.Notifications.IconImport.NothingImported(pack: pack),
					Resolve(SkippedNote(batch), culture)),
			IconImportBatchState.Completed =>
				(UserNotificationSeverity.Warning,
					AppStrings.Notifications.IconImport.CompletedWithWarnings(count: processed, pack: pack),
					batch.Error),
			IconImportBatchState.CompletedWithErrors =>
				(UserNotificationSeverity.Warning,
					AppStrings.Notifications.IconImport.PartlyFailed(
						imported: processed.ToString(CultureInfo.InvariantCulture),
						failed: failed.ToString(CultureInfo.InvariantCulture),
						pack: pack),
					batch.Error),
			IconImportBatchState.Failed =>
				(UserNotificationSeverity.Error, AppStrings.Notifications.IconImport.Failed(pack: pack), batch.Error),
			_ => (UserNotificationSeverity.Info, AppStrings.Notifications.IconImport.Importing(pack: pack), null)
		};

		if (batch.Silent && content.Severity is not (UserNotificationSeverity.Warning or UserNotificationSeverity.Error))
		{
			return null;
		}

		return new UserNotificationDraft
		{
			Severity = content.Severity,
			Kind = UserNotificationKind.IconImport,
			Title = Resolve(content.Title, culture),
			Message = content.Message,
			Action = new UserNotificationAction(UserNotificationActionKind.OpenIconPacks, batch.PackId.ToString()),
			Progress = isTerminal ? null : new UserNotificationProgress(processed, total),
			CancelKey = isTerminal ? null : batch.Id.ToString(),
			DedupeKey = DedupeKey(batch)
		};
	}

	private string Resolve(LocalizedText text, string? culture) => _localization.Resolve(text, culture) ?? string.Empty;

	private static string DedupeKey(IconImportBatchEntity batch) => $"icon-import:{batch.Id}";
}
