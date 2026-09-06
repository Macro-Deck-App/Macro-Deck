using MacroDeckHost.Application.Backups;
using MacroDeckHost.Application.Backups.Storage;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Backups;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Ui.Handlers;

public sealed class InspectBackupRequestMessageHandler
	: IUiTransportMessageHandler<InspectBackupRequest, InspectBackupResponse>
{
	private readonly IBackupService _backupService;
	private readonly IBackupStorageRegistry _storageRegistry;

	public InspectBackupRequestMessageHandler(IBackupService backupService, IBackupStorageRegistry storageRegistry)
	{
		_backupService = backupService;
		_storageRegistry = storageRegistry;
	}

	public async ValueTask<InspectBackupResponse> Handle(InspectBackupRequest request,
		CancellationToken cancellationToken)
	{
		var list = await _backupService.List(cancellationToken);
		var descriptor = list.Success ? list.Data!.FirstOrDefault(backup => backup.BackupId == request.BackupId) : null;
		if (descriptor is null)
		{
			return new InspectBackupResponse
			{
				Success = false,
				Error = BackupDtoMapper.ToTransportError(BackupError.NotFound, "The backup was not found.")
			};
		}

		var source = new BackupSourceRef(descriptor.ProviderId, descriptor.StorageId, null);
		var result = await _backupService.Inspect(source, request.RecoveryKey, cancellationToken);
		if (!result.Success)
		{
			return new InspectBackupResponse
			{
				Success = false,
				Error = BackupDtoMapper.ToTransportError(result.Error!.Value, result.ErrorMessage)
			};
		}

		var displayName = _storageRegistry.Find(descriptor.ProviderId)?.DisplayName ?? descriptor.ProviderId;

		return BackupDtoMapper.ToInspectResponse(result.Data!, displayName);
	}
}
