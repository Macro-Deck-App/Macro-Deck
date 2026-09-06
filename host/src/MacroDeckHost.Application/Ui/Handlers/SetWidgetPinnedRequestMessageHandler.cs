using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Ui.Handlers;

public class SetWidgetPinnedRequestMessageHandler
	: IUiTransportMessageHandler<SetWidgetPinnedRequest, SetWidgetPinnedResponse>
{
	private readonly IWidgetService _widgetService;

	public SetWidgetPinnedRequestMessageHandler(IWidgetService widgetService)
	{
		_widgetService = widgetService;
	}

	public async ValueTask<SetWidgetPinnedResponse> Handle(
		SetWidgetPinnedRequest request,
		CancellationToken cancellationToken)
	{
		if (!Guid.TryParse(request.WidgetId, out var widgetId) || !Guid.TryParse(request.FolderId, out var folderId))
		{
			return new SetWidgetPinnedResponse
			{
				Success = false,
				Error = new TransportError
					{ Code = "VALIDATION_ERROR", Message = AppStrings.Errors.Widgets.InvalidWidgetOrFolderId() }
			};
		}

		var result = await _widgetService.SetPinned(folderId, widgetId, request.Pinned, request.Scope);

		var response = new SetWidgetPinnedResponse { Success = result.Success };

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
