using MacroDeckHost.Application.Rendering;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Widgets;

public class LabelTextUpdatedEvent
{
	public string WidgetId { get; set; } = string.Empty;
	public string State { get; set; } = "off";
	public string? Text { get; set; }

	// Omitted from the wire when the state has no extra labels, so a client that predates them sees no change.
	public IReadOnlyList<string?>? ExtraTexts { get; set; }

	public static LabelTextUpdatedEvent From(string widgetId, string state, LabelTexts texts)
	{
		ArgumentNullException.ThrowIfNull(texts);

		return new LabelTextUpdatedEvent
		{
			WidgetId = widgetId,
			State = state,
			Text = texts.Primary,
			ExtraTexts = texts.Extras.Count > 0 ? texts.Extras : null
		};
	}
}
