using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Ui.Handlers;

public class CreateWidgetRequestMessageHandler : IUiTransportMessageHandler<CreateWidgetRequest, CreateWidgetResponse>
{
	private readonly IWidgetService _widgetService;

	public CreateWidgetRequestMessageHandler(IWidgetService widgetService)
	{
		_widgetService = widgetService;
	}

	public async ValueTask<CreateWidgetResponse> Handle(
		CreateWidgetRequest request,
		CancellationToken cancellationToken)
	{
		var folderId = Guid.Parse(request.FolderId);

		Guid? sourceWidgetId = null;
		if (!string.IsNullOrWhiteSpace(request.SourceWidgetId))
		{
			if (!Guid.TryParse(request.SourceWidgetId, out var parsedSourceWidgetId))
			{
				return new CreateWidgetResponse
				{
					Success = false,
					Error = new TransportError { Code = "VALIDATION_ERROR", Message = "Invalid source widget ID" }
				};
			}

			sourceWidgetId = parsedSourceWidgetId;
		}

		var widget = new WidgetEntity
		{
			Type = request.Type,
			PositionX = request.PositionX,
			PositionY = request.PositionY,
			Width = request.Width,
			Height = request.Height,
			Data = request.Data
		};

		var result = await _widgetService.Create(folderId, widget, sourceWidgetId);

		var response = new CreateWidgetResponse { Success = result.Success };

		if (result.Success)
		{
			response.Widget = FolderDtoMapper.MapWidgetToDto(result.Data!);
		}
		else
		{
			response.Error = new TransportError
			{
				Code = result.Error.ToString()!,
				Message = result.ErrorMessage ?? string.Empty
			};
		}

		return response;
	}
}
