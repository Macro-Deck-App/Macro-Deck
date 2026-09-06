using MacroDeckHost.Application.Backups;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Backups;

namespace MacroDeckHost.Application.Ui.Handlers;

public sealed class CommitRestoreRequestMessageHandler
	: IUiTransportMessageHandler<CommitRestoreRequest, CommitRestoreResponse>
{
	private readonly IRestoreService _restoreService;

	public CommitRestoreRequestMessageHandler(IRestoreService restoreService) => _restoreService = restoreService;

	public async ValueTask<CommitRestoreResponse> Handle(CommitRestoreRequest request,
		CancellationToken cancellationToken)
	{
		var result = await _restoreService.Commit(request.RestoreId, cancellationToken);
		if (!result.Success)
		{
			return new CommitRestoreResponse
			{
				Success = false,
				Error = BackupDtoMapper.ToTransportError(result.Error!.Value, result.ErrorMessage)
			};
		}

		var commit = result.Data!;

		return new CommitRestoreResponse
		{
			Success = true,
			RestartRequested = commit.RestartRequested,
			RestartSupported = commit.RestartSupported,
			RestartUnavailableReason = commit.RestartUnavailableReason
		};
	}
}
