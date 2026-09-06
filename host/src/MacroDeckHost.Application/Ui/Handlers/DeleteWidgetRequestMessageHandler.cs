using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;

namespace MacroDeckHost.Application.Ui.Handlers;

public class DeleteWidgetRequestMessageHandler : IUiTransportMessageHandler<DeleteWidgetRequest, DeleteWidgetResponse>
{
	private readonly IWidgetService _widgetService;

	public DeleteWidgetRequestMessageHandler(IWidgetService widgetService)
	{
		_widgetService = widgetService;
	}

	public async ValueTask<DeleteWidgetResponse> Handle(
		DeleteWidgetRequest request,
		CancellationToken cancellationToken)
	{
		var widgetId = Guid.Parse(request.Id);
		var folderId = Guid.Parse(request.FolderId);

		var result = await _widgetService.Delete(widgetId, folderId);

		var response = new DeleteWidgetResponse { Success = result.Success };

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
}
