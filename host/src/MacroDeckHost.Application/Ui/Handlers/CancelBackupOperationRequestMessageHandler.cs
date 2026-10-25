using MacroDeckHost.Application.Backups;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Backups;

namespace MacroDeckHost.Application.Ui.Handlers;

public sealed class CancelBackupOperationRequestMessageHandler
	: IUiTransportMessageHandler<CancelBackupOperationRequest, CancelBackupOperationResponse>
{
	private readonly IBackupOperationCancellation _cancellation;

	public CancelBackupOperationRequestMessageHandler(IBackupOperationCancellation cancellation)
	{
		_cancellation = cancellation;
	}

	public ValueTask<CancelBackupOperationResponse> Handle(
		CancelBackupOperationRequest request,
		CancellationToken cancellationToken)
	{
		return new ValueTask<CancelBackupOperationResponse>(new CancelBackupOperationResponse
		{
			Success = _cancellation.Cancel(request.OperationId)
		});
	}
}
