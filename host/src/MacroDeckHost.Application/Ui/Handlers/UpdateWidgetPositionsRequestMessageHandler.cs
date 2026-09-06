using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using MacroDeckHost.Domain.Common;

namespace MacroDeckHost.Application.Ui.Handlers;

public class UpdateWidgetPositionsRequestMessageHandler
	: IUiTransportMessageHandler<UpdateWidgetPositionsRequest, UpdateWidgetPositionsResponse>
{
	private readonly IWidgetService _widgetService;

	public UpdateWidgetPositionsRequestMessageHandler(IWidgetService widgetService)
	{
		_widgetService = widgetService;
	}

	public async ValueTask<UpdateWidgetPositionsResponse> Handle(
		UpdateWidgetPositionsRequest request,
		CancellationToken cancellationToken)
	{
		if (!Guid.TryParse(request.FolderId, out var folderId))
		{
			return ValidationError("Invalid folder ID");
		}

		var placements = new List<WidgetPlacement>(request.Positions.Count);
		foreach (var position in request.Positions)
		{
			if (!Guid.TryParse(position.Id, out var widgetId))
			{
				return ValidationError("Invalid widget ID");
			}

			placements.Add(new WidgetPlacement(widgetId,
				position.PositionX,
				position.PositionY,
				position.Width,
				position.Height));
		}

		var result = await _widgetService.UpdatePositions(folderId, placements);

		var response = new UpdateWidgetPositionsResponse { Success = result.Success };

		if (result.Success)
		{
			response.Widgets = result.Data!.Select(widget => FolderDtoMapper.MapWidgetToDto(widget)).ToList();
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

	private static UpdateWidgetPositionsResponse ValidationError(string message)
	{
		return new UpdateWidgetPositionsResponse
		{
			Success = false,
			Error = new TransportError { Code = "VALIDATION_ERROR", Message = message }
		};
	}
}
