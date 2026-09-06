namespace MacroDeck.Sdk.Widgets;

/// <summary>
/// One property of a <see cref="WidgetAppearancePatch" /> that can be cleared independently of the
/// rest of it - see <see cref="WidgetAppearanceRequest.ClearProperties" />. An action whose control
/// offers "reset to default" alongside "set a value" reports the property here instead of writing a
/// value the host would have to invent.
///
/// Values are persisted and exchanged through the SDK, so they are explicitly assigned and new
/// entries must be appended. The original BackgroundColor value is retained for binary consumers.
/// </summary>
public enum WidgetAppearanceProperty
{
	BackgroundColor = 0,
	Label = 1,
	LabelColor = 2,
	Icon = 3,
	Font = 4,
	Border = 5,
	BorderColor = 6,

	/// <summary>
	/// How the icon is framed - fit mode, zoom, position and opacity. One property rather than five,
	/// because a widget type either renders a framed icon or none of it, and clearing it means
	/// "back to the defaults" for the whole framing.
	/// </summary>
	IconDisplay = 7
}
