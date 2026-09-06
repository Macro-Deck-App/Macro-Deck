using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Icons;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Ui.Handlers;

public class ImportIconsFromPathRequestMessageHandler
	: IUiTransportMessageHandler<ImportIconsFromPathRequest, ImportIconsResponse>
{
	private readonly IIconImportService _iconImportService;
	private readonly IconImportBatchTracker _batchTracker;

	public ImportIconsFromPathRequestMessageHandler(IIconImportService iconImportService,
		IconImportBatchTracker batchTracker)
	{
		_iconImportService = iconImportService;
		_batchTracker = batchTracker;
	}

	public async ValueTask<ImportIconsResponse> Handle(ImportIconsFromPathRequest request,
		CancellationToken cancellationToken)
	{
		Guid? packId = null;
		if (!string.IsNullOrEmpty(request.PackId))
		{
			if (!Guid.TryParse(request.PackId, out var parsed))
			{
				return new ImportIconsResponse
				{
					Success = false,
					Error = new TransportError
					{
						Code = nameof(IconError.ValidationError),
						Message = AppStrings.Errors.Icons.PackIdRequired()
					}
				};
			}

			packId = parsed;
		}

		var paths = request.Paths?.Where(p => !string.IsNullOrWhiteSpace(p)).ToList() ?? [];
		if (paths.Count == 0)
		{
			return new ImportIconsResponse
			{
				Success = false,
				Error = new TransportError
				{
					Code = nameof(IconError.ValidationError),
					Message = AppStrings.Errors.Icons.AtLeastOnePathRequired()
				}
			};
		}

		var result = await _iconImportService.ImportFromPath(packId, paths, cancellationToken);
		return IconMapper.ToImportResponse(result, _batchTracker);
	}
}
