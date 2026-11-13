using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Ui.Handlers;

public class CopyWidgetStyleRequestMessageHandler
	: IUiTransportMessageHandler<CopyWidgetStyleRequest, CopyWidgetStyleResponse>
{
	private readonly IWidgetStyleService _widgetStyles;

	public CopyWidgetStyleRequestMessageHandler(IWidgetStyleService widgetStyles)
	{
		_widgetStyles = widgetStyles;
	}

	public ValueTask<CopyWidgetStyleResponse> Handle(
		CopyWidgetStyleRequest request,
		CancellationToken cancellationToken)
	{
		if (!Guid.TryParse(request.WidgetId, out var widgetId))
		{
			return ValueTask.FromResult(new CopyWidgetStyleResponse
			{
				Error = new TransportError
				{
					Code = "VALIDATION_ERROR", Message = AppStrings.Errors.Widgets.InvalidId()
				}
			});
		}

		var style = _widgetStyles.Copy(widgetId);
		if (style is null)
		{
			return ValueTask.FromResult(new CopyWidgetStyleResponse
			{
				Error = new TransportError { Code = "NOT_FOUND", Message = AppStrings.Errors.Widgets.NotFound() }
			});
		}

		return ValueTask.FromResult(new CopyWidgetStyleResponse { Success = true, Style = style });
	}
}
