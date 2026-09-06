using MacroDeckHost.Application.Backups;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Backups;

namespace MacroDeckHost.Application.Ui.Handlers;

public sealed class CreateBackupRecoveryKeyRequestMessageHandler
	: IUiTransportMessageHandler<CreateBackupRecoveryKeyRequest, BackupRecoveryKeyResponse>
{
	private readonly IBackupRecoveryKeyService _recoveryKeyService;

	public CreateBackupRecoveryKeyRequestMessageHandler(IBackupRecoveryKeyService recoveryKeyService)
		=> _recoveryKeyService = recoveryKeyService;

	public async ValueTask<BackupRecoveryKeyResponse> Handle(
		CreateBackupRecoveryKeyRequest request,
		CancellationToken cancellationToken)
	{
		var state = await _recoveryKeyService.GetState(cancellationToken);

		// A key is only ever revealed here at the moment it is first minted: nothing has seen it yet, so
		// the user must be shown it now or lose access to it. Once one already exists, this endpoint - on
		// the public listener - must behave like a state read, never a repeatable way to fetch the secret.
		if (state.Availability == BackupRecoveryKeyAvailability.Available)
		{
			return BackupDtoMapper.ToRecoveryKeyResponse(state, null);
		}

		var ensured = await _recoveryKeyService.EnsureCreated(cancellationToken);
		if (!ensured.Success)
		{
			return BackupDtoMapper.ToRecoveryKeyFailure(ensured.Error!.Value, ensured.ErrorMessage);
		}

		var exported = await _recoveryKeyService.Export(cancellationToken);
		if (!exported.Success)
		{
			return BackupDtoMapper.ToRecoveryKeyFailure(exported.Error!.Value, exported.ErrorMessage);
		}

		var updatedState = await _recoveryKeyService.GetState(cancellationToken);
		return BackupDtoMapper.ToRecoveryKeyResponse(updatedState, exported.Data);
	}
}
