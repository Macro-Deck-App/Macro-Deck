using MacroDeckHost.Application.Backups;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Backups;

namespace MacroDeckHost.Application.Ui.Handlers;

public sealed class AcknowledgeBackupRecoveryKeyRequestMessageHandler
	: IUiTransportMessageHandler<AcknowledgeBackupRecoveryKeyRequest, BackupRecoveryKeyResponse>
{
	private readonly IBackupRecoveryKeyService _recoveryKeyService;

	public AcknowledgeBackupRecoveryKeyRequestMessageHandler(IBackupRecoveryKeyService recoveryKeyService)
		=> _recoveryKeyService = recoveryKeyService;

	public async ValueTask<BackupRecoveryKeyResponse> Handle(
		AcknowledgeBackupRecoveryKeyRequest request,
		CancellationToken cancellationToken)
	{
		var result = await _recoveryKeyService.Acknowledge(cancellationToken);
		if (!result.Success)
		{
			return BackupDtoMapper.ToRecoveryKeyFailure(result.Error!.Value, result.ErrorMessage);
		}

		var state = await _recoveryKeyService.GetState(cancellationToken);
		return BackupDtoMapper.ToRecoveryKeyResponse(state, null);
	}
}
