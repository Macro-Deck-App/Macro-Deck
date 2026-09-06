using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;

namespace MacroDeckHost.Application.Ui.Handlers;

public class DeleteWidgetsRequestMessageHandler
	: IUiTransportMessageHandler<DeleteWidgetsRequest, DeleteWidgetsResponse>
{
	private readonly IWidgetService _widgetService;

	public DeleteWidgetsRequestMessageHandler(IWidgetService widgetService)
	{
		_widgetService = widgetService;
	}

	public async ValueTask<DeleteWidgetsResponse> Handle(
		DeleteWidgetsRequest request,
		CancellationToken cancellationToken)
	{
		if (!Guid.TryParse(request.FolderId, out var folderId))
		{
			return ValidationError("Invalid folder ID");
		}

		var widgetIds = new List<Guid>(request.Ids.Count);
		foreach (var id in request.Ids)
		{
			if (!Guid.TryParse(id, out var widgetId))
			{
				return ValidationError("Invalid widget ID");
			}

			widgetIds.Add(widgetId);
		}

		var result = await _widgetService.DeleteMany(folderId, widgetIds);

		var response = new DeleteWidgetsResponse { Success = result.Success };

		if (!result.Success)
		{
			response.Error = new TransportError
			{
				Code = result.Error.ToString()!,
				Message = result.ErrorMessage ?? string.Empty
			};
		}

		return response;
	}

	private static DeleteWidgetsResponse ValidationError(string message)
		=> new()
		{
			Success = false,
			Error = new TransportError { Code = "VALIDATION_ERROR", Message = message }
		};
}
