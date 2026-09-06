using MacroDeckHost.Application.Backups;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Backups;

namespace MacroDeckHost.Application.Ui.Handlers;

public sealed class GetBackupStatusRequestMessageHandler
	: IUiTransportMessageHandler<GetBackupStatusRequest, GetBackupStatusResponse>
{
	private readonly IBackupProgressReporter _progressReporter;

	public GetBackupStatusRequestMessageHandler(IBackupProgressReporter progressReporter)
		=> _progressReporter = progressReporter;

	public ValueTask<GetBackupStatusResponse> Handle(GetBackupStatusRequest request,
		CancellationToken cancellationToken)
	{
		var status = _progressReporter.Current;

		return new ValueTask<GetBackupStatusResponse>(new GetBackupStatusResponse
		{
			OperationId = status.OperationId,
			Kind = status.Kind.ToString(),
			Stage = status.Stage.ToString(),
			Trigger = status.Trigger.ToString(),
			PercentComplete = status.PercentComplete,
			BytesProcessed = status.BytesProcessed,
			TotalBytes = status.TotalBytes,
			BackupId = status.BackupId,
			Error = status.Error?.ToString(),
			ErrorMessage = status.ErrorMessage,
			UpdatedAt = status.UpdatedAt
		});
	}
}
