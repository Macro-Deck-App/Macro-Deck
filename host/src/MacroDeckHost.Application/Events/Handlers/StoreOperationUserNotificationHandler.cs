using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Store.Operations;
using MacroDeck.Localization;
using MacroDeckHost.Localization;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Application.Events.Handlers;

/// <summary>Raises a per-operation notification only once an install or update reaches a terminal state -
/// intermediate progress belongs on the store page, not the notification list. The dedupe key carries the
/// package id so two packages failing around the same time do not overwrite each other's notification.
/// </summary>
public sealed class StoreOperationUserNotificationHandler : INotificationHandler<StoreOperationProgressNotification>
{
	private readonly IUserNotificationStore _store;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ILocalizationResolver _localization;

	public StoreOperationUserNotificationHandler(
		IUserNotificationStore store,
		IServiceScopeFactory scopeFactory,
		ILocalizationResolver localization)
	{
		_store = store;
		_scopeFactory = scopeFactory;
		_localization = localization;
	}

	public async ValueTask Handle(StoreOperationProgressNotification notification, CancellationToken cancellationToken)
	{
		var operation = notification.Operation;
		if (!operation.IsTerminal)
		{
			return;
		}

		var dedupeKey = DedupeKey(operation);
		if (operation.State is StoreOperationState.Cancelled)
		{
			_store.DismissByKey(dedupeKey);
			return;
		}

		var culture = await ActiveLocalization.Culture(_scopeFactory);
		var isUpdate = operation.Kind is StoreOperationKind.Update;
		var draft = operation.State is StoreOperationState.Completed
			? new UserNotificationDraft
			{
				Severity = UserNotificationSeverity.Info,
				Kind = UserNotificationKind.Update,
				Title = _localization.Resolve(isUpdate
							? AppStrings.Notifications.StoreExtensionUpdated(name: operation.DisplayName)
							: AppStrings.Notifications.StoreExtensionInstalled(name: operation.DisplayName),
						culture) ??
					operation.DisplayName,
				Message = _localization.Resolve(AppStrings.Notifications.StoreVersionReady(version: operation.Version),
					culture),
				Action = new UserNotificationAction(UserNotificationActionKind.OpenExtensionStore, operation.PackageId),
				DedupeKey = dedupeKey
			}
			: new UserNotificationDraft
			{
				Severity = UserNotificationSeverity.Error,
				Kind = UserNotificationKind.Error,
				Title = _localization.Resolve(isUpdate
							? AppStrings.Notifications.StoreExtensionUpdateFailed(name: operation.DisplayName)
							: AppStrings.Notifications.StoreExtensionInstallFailed(name: operation.DisplayName),
						culture) ??
					operation.DisplayName,
				Message = operation.ErrorMessage,
				Action = new UserNotificationAction(UserNotificationActionKind.OpenExtensionStore, operation.PackageId),
				DedupeKey = dedupeKey
			};

		_store.Raise(draft);
	}

	private static string DedupeKey(StoreOperation operation) =>
		$"store-operation:{operation.ExtensionKind}:{operation.PackageId.ToLowerInvariant()}";
}
