namespace MacroDeck.Sdk.Widgets;

/// <summary>
/// Ids of the host-backed option sources the widget actions pick from. They live in the SDK because
/// an integration declares the parameter and the host answers it, so the string is a contract
/// between the two rather than an implementation detail of either.
/// </summary>
public static class WidgetOptionsSources
{
	/// <summary>Every widget the user has, for a widget-target parameter.</summary>
	public const string Widgets = "macrodeck.widgets";

	/// <summary>
	/// The host's font faces. Each option value is a stable face id - a specific weight/width/slant of
	/// a family, not the family alone - so a choice never has to guess which style it actually got.
	/// </summary>
	public const string Fonts = "macrodeck.fonts";
}
