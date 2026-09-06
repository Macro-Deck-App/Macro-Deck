using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Backups;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

// Backup events go to the admin group rather than every connected client: a deck device has no use for
// backup ids, storage names or failure messages, and they describe the installation's own data.
public sealed class BackupOperationProgressNotificationHandler
	: INotificationHandler<BackupOperationProgressNotification>
{
	private readonly IUiTransport _uiTransport;

	public BackupOperationProgressNotificationHandler(IUiTransport uiTransport) => _uiTransport = uiTransport;

	public async ValueTask Handle(BackupOperationProgressNotification notification,
		CancellationToken cancellationToken)
	{
		var status = notification.Status;

		await _uiTransport.SendToGroup(UiAdminGroups.Admin,
			new BackupOperationProgressEvent
			{
				OperationId = status.OperationId,
				Kind = status.Kind.ToString(),
				Stage = status.Stage.ToString(),
				Trigger = status.Trigger.ToString(),
				PercentComplete = status.PercentComplete,
				BytesProcessed = status.BytesProcessed,
				TotalBytes = status.TotalBytes,
				BackupId = status.BackupId,
				Error = status.Error?.ToString(),
				ErrorMessage = status.ErrorMessage,
				UpdatedAt = status.UpdatedAt
			},
			cancellationToken);
	}
}

public sealed class BackupListChangedNotificationHandler : INotificationHandler<BackupListChangedNotification>
{
	private readonly IUiTransport _uiTransport;

	public BackupListChangedNotificationHandler(IUiTransport uiTransport) => _uiTransport = uiTransport;

	public ValueTask Handle(BackupListChangedNotification notification, CancellationToken cancellationToken)
		=> new(_uiTransport.SendToGroup(UiAdminGroups.Admin,
			new BackupListChangedEvent { Reason = notification.Reason },
			cancellationToken));
}

public sealed class BackupRecoveryKeyStateChangedNotificationHandler
	: INotificationHandler<BackupRecoveryKeyStateChangedNotification>
{
	private readonly IUiTransport _uiTransport;

	public BackupRecoveryKeyStateChangedNotificationHandler(IUiTransport uiTransport) => _uiTransport = uiTransport;

	public ValueTask Handle(BackupRecoveryKeyStateChangedNotification notification,
		CancellationToken cancellationToken)
		=> new(_uiTransport.SendToGroup(UiAdminGroups.Admin,
			new BackupRecoveryKeyStateChangedEvent
			{
				State = notification.State.Availability.ToString(),
				KeyId = notification.State.KeyId,
				ExportedAt = notification.State.ExportedAt
			},
			cancellationToken));
}

public sealed class RestorePendingChangedNotificationHandler : INotificationHandler<RestorePendingChangedNotification>
{
	private readonly IUiTransport _uiTransport;

	public RestorePendingChangedNotificationHandler(IUiTransport uiTransport) => _uiTransport = uiTransport;

	public ValueTask Handle(RestorePendingChangedNotification notification, CancellationToken cancellationToken)
		=> new(_uiTransport.SendToGroup(UiAdminGroups.Admin,
			new RestorePendingEvent
			{
				Pending = notification.Pending is not null,
				RestoreId = notification.Pending?.RestoreId,
				BackupId = notification.Pending?.BackupId,
				RestartSupported = notification.RestartSupported,
				Components = BackupDtoMapper.ToNames(notification.Pending?.Components ?? [])
			},
			cancellationToken));
}
