using MacroDeck.Localization;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Ui.Handlers;

public class SetWidgetsPinnedRequestMessageHandler
	: IUiTransportMessageHandler<SetWidgetsPinnedRequest, SetWidgetsPinnedResponse>
{
	private readonly IWidgetService _widgetService;

	public SetWidgetsPinnedRequestMessageHandler(IWidgetService widgetService)
	{
		_widgetService = widgetService;
	}

	public async ValueTask<SetWidgetsPinnedResponse> Handle(
		SetWidgetsPinnedRequest request,
		CancellationToken cancellationToken)
	{
		if (!Guid.TryParse(request.FolderId, out var folderId))
		{
			return ValidationError(AppStrings.Errors.Folders.InvalidId());
		}

		var widgetIds = new List<Guid>(request.WidgetIds.Count);
		foreach (var id in request.WidgetIds)
		{
			if (!Guid.TryParse(id, out var widgetId))
			{
				return ValidationError(AppStrings.Errors.Widgets.InvalidId());
			}

			widgetIds.Add(widgetId);
		}

		var result = await _widgetService.SetPinnedMany(folderId, widgetIds, request.Pinned, request.Scope);

		var response = new SetWidgetsPinnedResponse { Success = result.Success };

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

	private static SetWidgetsPinnedResponse ValidationError(LocalizedText message)
		=> new()
		{
			Success = false,
			Error = new TransportError { Code = "VALIDATION_ERROR", Message = message }
		};
}
