namespace MacroDeck.Sdk.Widgets;

/// <summary>
/// Sentinel values a widget-appearance state id can carry besides a real one, shared by the SDK, the
/// host and the Angular picker. Mirrors <see cref="WidgetTargets" /> and <see cref="WidgetAppearanceValues" />.
/// </summary>
public static class WidgetStates
{
	/// <summary>Stored value meaning "whichever state the widget shows right now".</summary>
	public const string Current = "$current";

	/// <summary>Stored value meaning "every state the widget has".</summary>
	public const string All = "$all";

	public static bool IsCurrent(string? value)
		=> string.Equals(value?.Trim(), Current, StringComparison.OrdinalIgnoreCase);

	public static bool IsAll(string? value)
		=> string.Equals(value?.Trim(), All, StringComparison.OrdinalIgnoreCase);
}

/// <summary>One state a widget can be in, as exposed to integration pickers.</summary>
/// <param name="Id">Stable across reconfiguration - see <c>MacroDeck.Sdk.Actions.ActionStateDefinition.Id</c>.</param>
/// <param name="Label">Display label for this state.</param>
public sealed record WidgetStateInfo(string Id, string Label);
