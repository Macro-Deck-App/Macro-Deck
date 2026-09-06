using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Backups;
using MacroDeckHost.Domain.Enums;
using AppBackups = MacroDeckHost.Application.Backups;

namespace MacroDeckHost.Application.Ui.Handlers;

public sealed class PrepareRestoreRequestMessageHandler
	: IUiTransportMessageHandler<PrepareRestoreRequest, PrepareRestoreResponse>
{
	private readonly AppBackups.IRestoreService _restoreService;
	private readonly AppBackups.IBackupService _backupService;

	public PrepareRestoreRequestMessageHandler(AppBackups.IRestoreService restoreService,
		AppBackups.IBackupService backupService)
	{
		_restoreService = restoreService;
		_backupService = backupService;
	}

	public async ValueTask<PrepareRestoreResponse> Handle(PrepareRestoreRequest request,
		CancellationToken cancellationToken)
	{
		var list = await _backupService.List(cancellationToken);
		var descriptor = list.Success ? list.Data!.FirstOrDefault(backup => backup.BackupId == request.BackupId) : null;
		if (descriptor is null)
		{
			return new PrepareRestoreResponse
			{
				Success = false,
				Error = BackupDtoMapper.ToTransportError(BackupError.NotFound, "The backup was not found.")
			};
		}

		if (!BackupDtoMapper.TryParseGroups(request.Components, out var components))
		{
			return new PrepareRestoreResponse
			{
				Success = false,
				Error = BackupDtoMapper.ToTransportError(BackupError.ValidationError,
					"The restore selection contains a component this version does not know.")
			};
		}

		var domainRequest
			= new AppBackups.PrepareRestoreRequest(request.BackupId, components, request.RecoveryKey);
		var result = await _restoreService.Prepare(domainRequest,
			request.ProceedWithoutSafetyBackup,
			cancellationToken);
		if (!result.Success)
		{
			return new PrepareRestoreResponse
			{
				Success = false,
				Error = BackupDtoMapper.ToTransportError(result.Error!.Value, result.ErrorMessage)
			};
		}

		var prepared = result.Data!;

		return new PrepareRestoreResponse
		{
			Success = true,
			RestoreId = prepared.RestoreId,
			BackupId = prepared.BackupId,
			Effective = BackupDtoMapper.ToNames(prepared.Effective),
			AutoSelected = BackupDtoMapper.ToNames(prepared.AutoSelected),
			Warnings = BackupDtoMapper.ToWarnings(prepared.Warnings),
			RecoveryKeyRequired = !descriptor.DecryptableLocally,
			SafetyBackupId = prepared.SafetyBackupId,
			SafetyBackupCreated = prepared.SafetyBackupCreated,
			Catalog = BackupDtoMapper.ToCatalog()
		};
	}
}
