using MacroDeckHost.Application.Backups;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Backups;

namespace MacroDeckHost.Application.Ui.Handlers;

public sealed class DeleteBackupRequestMessageHandler
	: IUiTransportMessageHandler<DeleteBackupRequest, DeleteBackupResponse>
{
	private readonly IBackupService _backupService;

	public DeleteBackupRequestMessageHandler(IBackupService backupService) => _backupService = backupService;

	public async ValueTask<DeleteBackupResponse> Handle(DeleteBackupRequest request,
		CancellationToken cancellationToken)
	{
		var result = await _backupService.Delete(request.BackupId, cancellationToken);

		return new DeleteBackupResponse
		{
			Success = result.Success,
			Error = result.Success ? null : BackupDtoMapper.ToTransportError(result.Error!.Value, result.ErrorMessage)
		};
	}
}
