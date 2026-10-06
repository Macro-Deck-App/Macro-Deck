using MacroDeck.Localization;

namespace MacroDeckHost.Widgets.Gauges;

internal sealed record GaugeFace
{
	public static readonly GaugeFace Empty = new();

	public double Level { get; init; }

	public string Value { get; init; } = GaugesViewStateResolver.Unavailable;

	public LocalizedText Unit { get; init; }

	public bool HasUnit => !Unit.IsEmpty;

	public string? Color { get; init; }

	public string Name { get; init; } = string.Empty;
}
