using MacroDeck.Localization;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Ui.Handlers;

public class PasteWidgetStyleRequestMessageHandler
	: IUiTransportMessageHandler<PasteWidgetStyleRequest, PasteWidgetStyleResponse>
{
	private readonly IWidgetStyleService _widgetStyles;

	public PasteWidgetStyleRequestMessageHandler(IWidgetStyleService widgetStyles)
	{
		_widgetStyles = widgetStyles;
	}

	public async ValueTask<PasteWidgetStyleResponse> Handle(
		PasteWidgetStyleRequest request,
		CancellationToken cancellationToken)
	{
		var widgetIds = new List<Guid>(request.WidgetIds.Count);
		foreach (var id in request.WidgetIds)
		{
			if (!Guid.TryParse(id, out var widgetId))
			{
				return Fail("VALIDATION_ERROR", AppStrings.Errors.Widgets.InvalidId());
			}

			widgetIds.Add(widgetId);
		}

		var outcome = await _widgetStyles.PasteAsync(request.Style, widgetIds, cancellationToken);
		return outcome switch
		{
			WidgetStylePasteOutcome.Applied => new PasteWidgetStyleResponse { Success = true },
			WidgetStylePasteOutcome.InvalidStyle => Fail("VALIDATION_ERROR", AppStrings.Errors.Widgets.StyleInvalid()),
			WidgetStylePasteOutcome.WidgetNotFound => Fail("NOT_FOUND", AppStrings.Errors.Widgets.NotFound()),
			WidgetStylePasteOutcome.Rejected => Fail("VALIDATION_ERROR", AppStrings.Errors.Widgets.StyleRejected()),
			_ => Fail("INTERNAL_ERROR", string.Empty)
		};
	}

	private static PasteWidgetStyleResponse Fail(string code, LocalizedText message)
	{
		return new PasteWidgetStyleResponse { Error = new TransportError { Code = code, Message = message } };
	}
}
