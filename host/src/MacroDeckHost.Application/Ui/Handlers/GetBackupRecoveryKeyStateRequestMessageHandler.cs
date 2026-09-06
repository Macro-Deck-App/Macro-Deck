using MacroDeckHost.Application.Backups;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Backups;

namespace MacroDeckHost.Application.Ui.Handlers;

public sealed class GetBackupRecoveryKeyStateRequestMessageHandler
	: IUiTransportMessageHandler<GetBackupRecoveryKeyStateRequest, GetBackupRecoveryKeyStateResponse>
{
	private readonly IBackupRecoveryKeyService _recoveryKeyService;

	public GetBackupRecoveryKeyStateRequestMessageHandler(IBackupRecoveryKeyService recoveryKeyService)
		=> _recoveryKeyService = recoveryKeyService;

	public async ValueTask<GetBackupRecoveryKeyStateResponse> Handle(
		GetBackupRecoveryKeyStateRequest request,
		CancellationToken cancellationToken)
	{
		var state = await _recoveryKeyService.GetState(cancellationToken);
		return BackupDtoMapper.ToStateResponse(state);
	}
}
