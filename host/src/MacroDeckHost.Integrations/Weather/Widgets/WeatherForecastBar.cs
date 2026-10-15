namespace MacroDeckHost.Integrations.Weather.Widgets;

internal static class WeatherForecastBar
{
	public static double? MarkerFraction(double? temperature, double weekMin, double span)
	{
		if (temperature is not { } value || double.IsNaN(value) || double.IsInfinity(value))
		{
			return null;
		}

		return Math.Clamp((value - weekMin) / span, 0, 1);
	}

	public static (double Start, double End) RangeBarSpan(double min, double max, double weekMin, double span)
	{
		var start = (min - weekMin) / span;
		var width = Math.Max(0.08, (max - min) / span);
		var end = Math.Min(1.0, start + width);

		return (start, end);
	}

	public static string TempColorHex(double value, string unit)
	{
		var celsius = string.Equals(unit, "fahrenheit", StringComparison.OrdinalIgnoreCase)
			? (value - 32) * 5 / 9
			: value;

		var clamped = Math.Clamp(celsius, -10, 40);
		var t = (clamped + 10) / 50;
		var hue = 220 - t * 220;

		return HslToHex(hue, 0.85, 0.55);
	}

	private static string HslToHex(double hue, double saturation, double lightness)
	{
		var h = (hue % 360 + 360) % 360;
		var chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
		var x = chroma * (1 - Math.Abs(h / 60 % 2 - 1));
		var m = lightness - chroma / 2;

		var (r1, g1, b1) = h switch
		{
			< 60 => (chroma, x, 0.0),
			< 120 => (x, chroma, 0.0),
			< 180 => (0.0, chroma, x),
			< 240 => (0.0, x, chroma),
			< 300 => (x, 0.0, chroma),
			_ => (chroma, 0.0, x)
		};

		var r = (byte)Math.Round((r1 + m) * 255, MidpointRounding.AwayFromZero);
		var g = (byte)Math.Round((g1 + m) * 255, MidpointRounding.AwayFromZero);
		var b = (byte)Math.Round((b1 + m) * 255, MidpointRounding.AwayFromZero);

		return $"#{r:x2}{g:x2}{b:x2}";
	}
}
