using MacroDeckHost.Application.Backups;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Backups;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Ui.Handlers;

public sealed class RevealBackupRecoveryKeyRequestMessageHandler
	: IUiTransportMessageHandler<RevealBackupRecoveryKeyRequest, BackupRecoveryKeyResponse>
{
	private readonly IBackupRecoveryKeyService _recoveryKeyService;

	public RevealBackupRecoveryKeyRequestMessageHandler(IBackupRecoveryKeyService recoveryKeyService)
		=> _recoveryKeyService = recoveryKeyService;

	public async ValueTask<BackupRecoveryKeyResponse> Handle(
		RevealBackupRecoveryKeyRequest request,
		CancellationToken cancellationToken)
	{
		if (!request.Confirm)
		{
			return BackupDtoMapper.ToRecoveryKeyFailure(BackupError.ValidationError,
				"Confirmation is required to reveal the recovery key.");
		}

		var exported = await _recoveryKeyService.Export(cancellationToken);
		if (!exported.Success)
		{
			return BackupDtoMapper.ToRecoveryKeyFailure(exported.Error!.Value, exported.ErrorMessage);
		}

		var state = await _recoveryKeyService.GetState(cancellationToken);
		return BackupDtoMapper.ToRecoveryKeyResponse(state, exported.Data);
	}
}
