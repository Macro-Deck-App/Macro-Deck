using System.Globalization;
using SkiaSharp;

namespace MacroDeckHost.Rendering.Paint;

internal sealed record GradientStop(SKColor Color, float Offset);

internal sealed record CssGradient(
	bool Radial,
	double AngleDegrees,
	double CenterX,
	double CenterY,
	IReadOnlyList<GradientStop> Stops);

internal static class CssColor
{
	public static SKColor? Parse(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return null;
		}

		var text = value.Trim();
		if (text == "transparent")
		{
			return SKColors.Transparent;
		}

		if (text.StartsWith('#')
			&& SKColor.TryParse(text.Length == 9 ? "#" + text[7..9] + text[1..7] : text, out var hex))
		{
			return hex;
		}

		if (text.StartsWith("rgb", StringComparison.Ordinal))
		{
			var open = text.IndexOf('(', StringComparison.Ordinal);
			var parts = text[(open + 1)..^1].Split([',', ' ', '/'], StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length < 3)
			{
				return null;
			}

			byte Channel(string part)
			{
				return (byte)Math.Clamp(Math.Round(double.Parse(part, CultureInfo.InvariantCulture)), 0, 255);
			}

			var alpha = parts.Length > 3 ? double.Parse(parts[3], CultureInfo.InvariantCulture) : 1;
			return new SKColor(Channel(parts[0]),
				Channel(parts[1]),
				Channel(parts[2]),
				(byte)Math.Clamp(Math.Round(alpha * 255), 0, 255));
		}

		return text switch
		{
			"white" => SKColors.White,
			"black" => SKColors.Black,
			"red" => SKColors.Red,
			_ => null
		};
	}

	public static CssGradient? Gradient(string? value)
	{
		if (value is null)
		{
			return null;
		}

		var radial = value.StartsWith("radial-gradient(", StringComparison.Ordinal);
		if (!radial && !value.StartsWith("linear-gradient(", StringComparison.Ordinal))
		{
			return null;
		}

		var inner = value[(value.IndexOf('(', StringComparison.Ordinal) + 1)..^1];
		var parts = Dom.StyleSheet.SplitTopLevel(inner, ',').Select(part => part.Trim()).ToList();
		double angle = 180;
		var centerX = 0.5;
		var centerY = 0.5;
		var first = parts[0];
		if (first.EndsWith("deg", StringComparison.Ordinal))
		{
			angle = double.Parse(first[..^3], CultureInfo.InvariantCulture);
			parts.RemoveAt(0);
		}
		else if (first == "to right")
		{
			angle = 90;
			parts.RemoveAt(0);
		}
		else if (first.StartsWith("at ", StringComparison.Ordinal))
		{
			var position = first[3..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
			centerX = double.Parse(position[0].TrimEnd('%'), CultureInfo.InvariantCulture) / 100;
			centerY = double.Parse(position[1].TrimEnd('%'), CultureInfo.InvariantCulture) / 100;
			parts.RemoveAt(0);
		}

		var stops = new List<GradientStop>();
		for (var index = 0; index < parts.Count; index++)
		{
			var part = parts[index];
			var split = part.LastIndexOf(' ');
			var hasOffset = split > 0 && part.EndsWith('%') && !part.EndsWith(')');
			var color = Parse(hasOffset ? part[..split] : part) ?? SKColors.Transparent;
			var offset = hasOffset
				? float.Parse(part[(split + 1)..^1], CultureInfo.InvariantCulture) / 100
				: parts.Count == 1
					? 0
					: index / (float)(parts.Count - 1);
			stops.Add(new GradientStop(color, offset));
		}

		return stops.Count == 0 ? null : new CssGradient(radial, angle, centerX, centerY, stops);
	}

	public static SKShader Shader(CssGradient gradient, SKRect rect)
	{
		var colors = gradient.Stops.Select(stop => stop.Color).ToArray();
		var offsets = gradient.Stops.Select(stop => stop.Offset).ToArray();
		if (gradient.Radial)
		{
			var center = new SKPoint(rect.Left + (float)gradient.CenterX * rect.Width,
				rect.Top + (float)gradient.CenterY * rect.Height);
			var dx = Math.Max(center.X - rect.Left, rect.Right - center.X);
			var dy = Math.Max(center.Y - rect.Top, rect.Bottom - center.Y);
			var radius = (float)Math.Sqrt(dx * dx + dy * dy);
			return SKShader.CreateRadialGradient(center,
				Math.Max(radius, 0.001f),
				colors,
				offsets,
				SKShaderTileMode.Clamp);
		}

		// CSS gradient line: through the centre at the angle, long enough that the corners reach 0% and 100%.
		var radians = gradient.AngleDegrees * Math.PI / 180;
		var sin = Math.Sin(radians);
		var cos = Math.Cos(radians);
		var half = (Math.Abs(rect.Width * sin) + Math.Abs(rect.Height * cos)) / 2;
		var middle = new SKPoint(rect.MidX, rect.MidY);
		var start = new SKPoint((float)(middle.X - sin * half), (float)(middle.Y + cos * half));
		var end = new SKPoint((float)(middle.X + sin * half), (float)(middle.Y - cos * half));
		return SKShader.CreateLinearGradient(start, end, colors, offsets, SKShaderTileMode.Clamp);
	}
}
