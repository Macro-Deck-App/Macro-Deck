using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Ui.Handlers;

public class CreateWidgetsRequestMessageHandler
	: IUiTransportMessageHandler<CreateWidgetsRequest, CreateWidgetsResponse>
{
	private readonly IWidgetService _widgetService;

	public CreateWidgetsRequestMessageHandler(IWidgetService widgetService)
	{
		_widgetService = widgetService;
	}

	public async ValueTask<CreateWidgetsResponse> Handle(
		CreateWidgetsRequest request,
		CancellationToken cancellationToken)
	{
		if (!Guid.TryParse(request.FolderId, out var folderId))
		{
			return ValidationError("Invalid folder ID");
		}

		var widgets = request.Widgets.Select(item => new WidgetEntity
		{
			Type = item.Type,
			PositionX = item.PositionX,
			PositionY = item.PositionY,
			Width = item.Width,
			Height = item.Height,
			Data = item.Data
		}).ToList();

		List<Guid>? replaceIds = null;
		if (request.ReplaceIds is not null)
		{
			replaceIds = new List<Guid>(request.ReplaceIds.Count);
			foreach (var id in request.ReplaceIds)
			{
				if (!Guid.TryParse(id, out var replaceId))
				{
					return ValidationError("Invalid widget ID");
				}

				replaceIds.Add(replaceId);
			}
		}

		var sourceWidgetIds = new List<Guid?>(request.Widgets.Count);
		foreach (var item in request.Widgets)
		{
			if (string.IsNullOrWhiteSpace(item.SourceWidgetId))
			{
				sourceWidgetIds.Add(null);
				continue;
			}

			if (!Guid.TryParse(item.SourceWidgetId, out var sourceWidgetId))
			{
				return ValidationError("Invalid source widget ID");
			}

			sourceWidgetIds.Add(sourceWidgetId);
		}

		var result = await _widgetService.CreateMany(folderId, widgets, replaceIds, sourceWidgetIds);

		var response = new CreateWidgetsResponse { Success = result.Success };

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

	private static CreateWidgetsResponse ValidationError(string message)
		=> new()
		{
			Success = false,
			Error = new TransportError { Code = "VALIDATION_ERROR", Message = message }
		};
}
