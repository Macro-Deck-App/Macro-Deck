using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Icons;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetIconImportBatchRequestMessageHandler
	: IUiTransportMessageHandler<GetIconImportBatchRequest, GetIconImportBatchResponse>
{
	private readonly IconImportBatchTracker _batchTracker;

	public GetIconImportBatchRequestMessageHandler(IconImportBatchTracker batchTracker)
	{
		_batchTracker = batchTracker;
	}

	public ValueTask<GetIconImportBatchResponse> Handle(GetIconImportBatchRequest request,
		CancellationToken cancellationToken)
	{
		if (!Guid.TryParse(request.BatchId, out var batchId))
		{
			return ValueTask.FromResult(new GetIconImportBatchResponse());
		}

		var batch = _batchTracker.Get(batchId);
		if (batch is null)
		{
			return ValueTask.FromResult(new GetIconImportBatchResponse());
		}

		var (total, processed, failed) = _batchTracker.GetCounters(batchId);
		int? reportedTotal = batch.State == IconImportBatchState.Discovering ? null : total;
		return ValueTask.FromResult(new GetIconImportBatchResponse
		{
			Batch = IconMapper.ToDto(batch, reportedTotal, processed, failed)
		});
	}
}
