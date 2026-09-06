using MacroDeckHost.Application.Backups;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Backups;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Ui.Handlers;

public sealed class RegenerateBackupRecoveryKeyRequestMessageHandler
	: IUiTransportMessageHandler<RegenerateBackupRecoveryKeyRequest, BackupRecoveryKeyResponse>
{
	private readonly IBackupRecoveryKeyService _recoveryKeyService;

	public RegenerateBackupRecoveryKeyRequestMessageHandler(IBackupRecoveryKeyService recoveryKeyService)
		=> _recoveryKeyService = recoveryKeyService;

	public async ValueTask<BackupRecoveryKeyResponse> Handle(
		RegenerateBackupRecoveryKeyRequest request,
		CancellationToken cancellationToken)
	{
		if (!request.Confirm || !request.AcknowledgeExistingBackupsBecomeUnreadable)
		{
			return BackupDtoMapper.ToRecoveryKeyFailure(BackupError.ValidationError,
				"Regenerating the recovery key needs both confirmations, because every existing backup " +
				"then opens only with the key that was exported before.");
		}

		var regenerated = await _recoveryKeyService.Regenerate(cancellationToken);
		if (!regenerated.Success)
		{
			return BackupDtoMapper.ToRecoveryKeyFailure(regenerated.Error!.Value, regenerated.ErrorMessage);
		}

		var state = await _recoveryKeyService.GetState(cancellationToken);
		return BackupDtoMapper.ToRecoveryKeyResponse(state, regenerated.Data);
	}
}
