namespace MacroDeck.Sdk.Widgets;

/// <summary>
/// The vocabulary of a widget-target action parameter, shared by the SDK, the host resolver and the
/// Angular picker.
/// </summary>
public static class WidgetTargets
{
	/// <summary>
	/// Stored value meaning "the widget whose flow is running". A sentinel rather than an empty
	/// string because an empty value has to stay available as "nothing picked yet": a script's target
	/// is cleared and not-yet-chosen, and the two must not collapse into one state.
	/// </summary>
	public const string Self = "$self";

	public static bool IsSelf(string? value)
		=> string.Equals(value?.Trim(), Self, StringComparison.OrdinalIgnoreCase);
}
