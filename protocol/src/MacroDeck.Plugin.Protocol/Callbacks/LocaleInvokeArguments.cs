namespace MacroDeck.Plugin.Protocol.Callbacks;

/// <summary>
/// The result of <c>locale</c>/<c>time-of-day-format</c>: how the user wants a time of day written. A
/// culture the plugin cannot load, or an hour cycle it does not know, falls back to the plugin's current
/// culture and <c>h23</c>.
/// </summary>
public sealed record LocaleTimeOfDayFormatResult
{
	/// <summary>The culture name of the Macro Deck interface, for example <c>de-DE</c>.</summary>
	public string Culture { get; init; } = string.Empty;

	/// <summary><c>h12</c> or <c>h23</c>, spelled as in Unicode CLDR.</summary>
	public string HourCycle { get; init; } = string.Empty;
}
