using MacroDeckHost.Application.Backups;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Backups;

namespace MacroDeckHost.Application.Ui.Handlers;

public sealed class CancelRestoreRequestMessageHandler
	: IUiTransportMessageHandler<CancelRestoreRequest, CancelRestoreResponse>
{
	private readonly IRestoreService _restoreService;

	public CancelRestoreRequestMessageHandler(IRestoreService restoreService) => _restoreService = restoreService;

	public async ValueTask<CancelRestoreResponse> Handle(CancelRestoreRequest request,
		CancellationToken cancellationToken)
	{
		var result = await _restoreService.Cancel(request.RestoreId, cancellationToken);

		return new CancelRestoreResponse
		{
			Success = result.Success,
			Error = result.Success ? null : BackupDtoMapper.ToTransportError(result.Error!.Value, result.ErrorMessage)
		};
	}
}
