using System.Text.Json;
using MacroDeck.Ui.Config;

namespace MacroDeckHost.Widgets;

internal static class WidgetThresholds
{
	public const string EnabledKey = "thresholdsEnabled";

	public const string ValueKey = "thresholds";

	public const string Green = "#34c759";
	public const string Yellow = "#ffcc00";
	public const string Orange = "#ff9500";
	public const string Red = "#ff3b30";

	private static readonly (string Id, string Color, double Fraction)[] _defaultBands =
	[
		("green", Green, 0),
		("yellow", Yellow, 0.5),
		("orange", Orange, 0.75),
		("red", Red, 0.9)
	];

	public static UiThresholds Defaults(double min, double max)
	{
		var low = Math.Min(min, max);
		var span = Math.Abs(max - min);

		if (!double.IsFinite(low) || !double.IsFinite(span) || span <= 0)
		{
			low = 0;
			span = 100;
		}

		return new UiThresholds([
			.. _defaultBands.Select((band, index) => new UiThresholdBand(band.Id,
				band.Color,
				index == 0 ? null : low + band.Fraction * span))
		]);
	}

	public static bool ReadEnabled(JsonElement data)
	{
		return data.ValueKind == JsonValueKind.Object
			&& data.TryGetProperty(EnabledKey, out var enabled)
			&& enabled.ValueKind == JsonValueKind.True;
	}

	public static UiThresholds? ReadStored(JsonElement data)
	{
		return data.ValueKind == JsonValueKind.Object
			&& data.TryGetProperty(ValueKey, out var stored)
			&& UiThresholds.TryParse(stored, out var thresholds)
				? thresholds
				: null;
	}

	public static UiThresholds Effective(UiThresholds? stored, double min, double max)
	{
		return stored ?? Defaults(min, max);
	}
}
