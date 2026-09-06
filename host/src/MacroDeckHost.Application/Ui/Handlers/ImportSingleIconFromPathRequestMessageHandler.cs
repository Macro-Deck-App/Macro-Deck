using MacroDeck.Localization;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Icons;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Ui.Handlers;

public class ImportSingleIconFromPathRequestMessageHandler
	: IUiTransportMessageHandler<ImportSingleIconFromPathRequest, ImportSingleIconResponse>
{
	private readonly IAppIconExtractor _appIconExtractor;
	private readonly IIconImportService _iconImportService;

	public ImportSingleIconFromPathRequestMessageHandler(IAppIconExtractor appIconExtractor,
		IIconImportService iconImportService)
	{
		_appIconExtractor = appIconExtractor;
		_iconImportService = iconImportService;
	}

	public async ValueTask<ImportSingleIconResponse> Handle(ImportSingleIconFromPathRequest request,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(request.Path))
		{
			return Invalid(AppStrings.Errors.Icons.PathRequired());
		}

		Guid? packId = null;
		if (!string.IsNullOrEmpty(request.PackId))
		{
			if (!Guid.TryParse(request.PackId, out var parsed))
			{
				return Invalid(AppStrings.Errors.Icons.PackIdRequired());
			}

			packId = parsed;
		}

		if (!_appIconExtractor.CanExtract(request.Path))
		{
			return new ImportSingleIconResponse
			{
				Success = false,
				Error = new TransportError
				{
					Code = nameof(IconError.UnsupportedFormat),
					Message = AppStrings.Errors.Icons.UnsupportedSource(name: Path.GetFileName(request.Path))
				}
			};
		}

		var extracted = await _appIconExtractor.Extract(request.Path, cancellationToken);
		if (!extracted.Success)
		{
			return new ImportSingleIconResponse
			{
				Success = false,
				Error = new TransportError
				{
					Code = extracted.Error.ToString()!,
					Message = extracted.ErrorMessage ?? string.Empty
				}
			};
		}

		var icon = extracted.Data!;
		await using var content = new MemoryStream(icon.Content, writable: false);
		var result = await _iconImportService.ImportSingle(packId,
			new IconImportFile(icon.FileName, content),
			cancellationToken);

		return IconMapper.ToSingleImportResponse(result);
	}

	private static ImportSingleIconResponse Invalid(LocalizedText message)
		=> new()
		{
			Success = false,
			Error = new TransportError
			{
				Code = nameof(IconError.ValidationError),
				Message = message
			}
		};
}
