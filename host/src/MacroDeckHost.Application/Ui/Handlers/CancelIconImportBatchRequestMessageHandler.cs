using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Icons;

namespace MacroDeckHost.Application.Ui.Handlers;

public class CancelIconImportBatchRequestMessageHandler
	: IUiTransportMessageHandler<CancelIconImportBatchRequest, CancelIconImportBatchResponse>
{
	private readonly IIconImportService _iconImportService;

	public CancelIconImportBatchRequestMessageHandler(IIconImportService iconImportService)
	{
		_iconImportService = iconImportService;
	}

	public async ValueTask<CancelIconImportBatchResponse> Handle(CancelIconImportBatchRequest request,
		CancellationToken cancellationToken)
	{
		if (!Guid.TryParse(request.BatchId, out var batchId))
		{
			return new CancelIconImportBatchResponse { Cancelled = false };
		}

		var cancelled = await _iconImportService.CancelBatch(batchId, cancellationToken);
		return new CancelIconImportBatchResponse { Cancelled = cancelled };
	}
}
