using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Backups;
using MacroDeckHost.Domain.Enums;
using AppBackups = MacroDeckHost.Application.Backups;
using AppBackupsStorage = MacroDeckHost.Application.Backups.Storage;

namespace MacroDeckHost.Application.Ui.Handlers;

public sealed class CreateBackupRequestMessageHandler
	: IUiTransportMessageHandler<CreateBackupRequest, CreateBackupResponse>
{
	private readonly AppBackups.IBackupService _backupService;
	private readonly AppBackupsStorage.IBackupStorageRegistry _storageRegistry;

	public CreateBackupRequestMessageHandler(AppBackups.IBackupService backupService,
		AppBackupsStorage.IBackupStorageRegistry storageRegistry)
	{
		_backupService = backupService;
		_storageRegistry = storageRegistry;
	}

	public async ValueTask<CreateBackupResponse> Handle(CreateBackupRequest request,
		CancellationToken cancellationToken)
	{
		var domainRequest = new AppBackups.CreateBackupRequest(BackupTrigger.Manual, request.Note, request.Protected);
		var result = await _backupService.Create(domainRequest, cancellationToken);

		if (!result.Success)
		{
			return new CreateBackupResponse
			{
				Success = false,
				Error = BackupDtoMapper.ToTransportError(result.Error!.Value, result.ErrorMessage)
			};
		}

		var descriptor = result.Data!;
		var displayName = _storageRegistry.Find(descriptor.ProviderId)?.DisplayName ?? descriptor.ProviderId;

		return new CreateBackupResponse { Success = true, Backup = BackupDtoMapper.ToSummary(descriptor, displayName) };
	}
}
