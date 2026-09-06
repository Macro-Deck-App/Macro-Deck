namespace MacroDeck.Sdk.Widgets;

/// <summary>
/// Sentinel values a widget-appearance action parameter can carry besides an ordinary one, shared by
/// the SDK, the host and the Angular picker. Mirrors <see cref="WidgetTargets" />.
/// </summary>
public static class WidgetAppearanceValues
{
	/// <summary>
	/// Stored value meaning "clear this property" rather than "set it to this colour" - no colour
	/// picker can produce it, so it travels through the same string-valued parameter as a real colour
	/// without ever being mistaken for one.
	/// </summary>
	public const string Reset = "$reset";

	public static bool IsReset(string? value)
		=> string.Equals(value?.Trim(), Reset, StringComparison.OrdinalIgnoreCase);
}
