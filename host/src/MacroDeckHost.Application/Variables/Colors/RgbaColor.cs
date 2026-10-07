using System.Globalization;
using System.Text.RegularExpressions;

namespace MacroDeckHost.Application.Variables.Colors;

public readonly partial record struct RgbaColor(byte R, byte G, byte B, byte A = 255)
{
	public static readonly RgbaColor Black = new(0, 0, 0);

	public bool IsOpaque => A == 255;

	public static bool TryParse(string? text, out RgbaColor color)
	{
		color = default;
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}

		var value = text.Trim();
		return value[0] == '#' ? TryParseHex(value[1..], out color) : TryParseFunctional(value, out color);
	}

	public static RgbaColor? Parse(string? text) => TryParse(text, out var color) ? color : null;

	public static string? Canonicalize(string? text) => TryParse(text, out var color) ? color.ToString() : null;

	public override string ToString()
		=> IsOpaque
			? string.Create(CultureInfo.InvariantCulture, $"#{R:x2}{G:x2}{B:x2}")
			: string.Create(CultureInfo.InvariantCulture, $"#{R:x2}{G:x2}{B:x2}{A:x2}");

	public string ToOpaqueString() => (this with { A = 255 }).ToString();

	public RgbaColor Lighten(double percent)
		=> WithHls((h, l, s) => (h, l + ((1 - l) * Fraction(percent)), s));

	public RgbaColor Darken(double percent)
		=> WithHls((h, l, s) => (h, l * (1 - Fraction(percent)), s));

	public RgbaColor Saturate(double percent)
		=> WithHls((h, l, s) => (h, l, s + ((1 - s) * Fraction(percent))));

	public RgbaColor Desaturate(double percent)
		=> WithHls((h, l, s) => (h, l, s * (1 - Fraction(percent))));

	public RgbaColor ShiftHue(double degrees)
		=> WithHls((h, l, s) => (PositiveModulo((h * 360) + degrees, 360) / 360, l, s));

	public RgbaColor WithOpacity(double percent) => this with { A = ToByte(Fraction(percent)) };

	public RgbaColor IncreaseOpacity(double percent)
	{
		var alpha = A / 255d;
		return this with { A = ToByte(alpha + ((1 - alpha) * Fraction(percent))) };
	}

	public RgbaColor ReduceOpacity(double percent) => this with { A = ToByte(A / 255d * (1 - Fraction(percent))) };

	public RgbaColor Mix(RgbaColor other, double percent)
	{
		var t = Fraction(percent);
		return new RgbaColor(MixChannel(R, other.R, t),
			MixChannel(G, other.G, t),
			MixChannel(B, other.B, t),
			MixChannel(A, other.A, t));
	}

	private static byte MixChannel(byte from, byte to, double t)
		=> (byte)Math.Clamp(Math.Floor(from + ((to - from) * t) + 0.5), 0, 255);

	private static double Fraction(double percent) => Math.Clamp(double.IsNaN(percent) ? 0 : percent, 0, 100) / 100;

	private static double PositiveModulo(double value, double modulus)
	{
		var result = value % modulus;
		return result < 0 ? result + modulus : result;
	}

	private static byte ToByte(double unit) => (byte)Math.Clamp(Math.Floor((unit * 255) + 0.5), 0, 255);

	// Mirrors Python's colorsys rgb_to_hls and hls_to_rgb, which the shared fixture vectors were generated with.
	private RgbaColor WithHls(Func<double, double, double, (double H, double L, double S)> change)
	{
		var (h, l, s) = ToHls(R / 255d, G / 255d, B / 255d);
		var next = change(h, l, s);
		var (r, g, b) = FromHls(next.H, next.L, next.S);
		return new RgbaColor(ToByte(r), ToByte(g), ToByte(b), A);
	}

	private static (double H, double L, double S) ToHls(double r, double g, double b)
	{
		var max = Math.Max(r, Math.Max(g, b));
		var min = Math.Min(r, Math.Min(g, b));
		var sum = max + min;
		var range = max - min;
		var l = sum / 2;
		if (min == max)
		{
			return (0, l, 0);
		}

		var s = l <= 0.5 ? range / sum : range / (2 - max - min);
		var rc = (max - r) / range;
		var gc = (max - g) / range;
		var bc = (max - b) / range;
		var h = r == max
			? bc - gc
			: g == max
				? 2 + rc - bc
				: 4 + gc - rc;
		return (PositiveModulo(h / 6, 1), l, s);
	}

	private static (double R, double G, double B) FromHls(double h, double l, double s)
	{
		if (s == 0)
		{
			return (l, l, l);
		}

		var m2 = l <= 0.5 ? l * (1 + s) : l + s - (l * s);
		var m1 = (2 * l) - m2;
		return (Channel(m1, m2, h + (1d / 3)), Channel(m1, m2, h), Channel(m1, m2, h - (1d / 3)));
	}

	private static double Channel(double m1, double m2, double hue)
	{
		hue = PositiveModulo(hue, 1);
		if (hue < 1d / 6)
		{
			return m1 + ((m2 - m1) * hue * 6);
		}

		if (hue < 0.5)
		{
			return m2;
		}

		if (hue < 2d / 3)
		{
			return m1 + ((m2 - m1) * ((2d / 3) - hue) * 6);
		}

		return m1;
	}

	private static bool TryParseHex(string hex, out RgbaColor color)
	{
		color = default;
		if (hex.Length is not (3 or 4 or 6 or 8) || !hex.All(Uri.IsHexDigit))
		{
			return false;
		}

		if (hex.Length is 3 or 4)
		{
			color = new RgbaColor(Nibble(hex[0]),
				Nibble(hex[1]),
				Nibble(hex[2]),
				hex.Length == 4 ? Nibble(hex[3]) : (byte)255);
			return true;
		}

		color = new RgbaColor(Pair(hex, 0), Pair(hex, 2), Pair(hex, 4), hex.Length == 8 ? Pair(hex, 6) : (byte)255);
		return true;
	}

	private static byte Nibble(char c) => (byte)(Convert.ToInt32(c.ToString(), 16) * 17);

	private static byte Pair(string hex, int index)
		=> byte.Parse(hex.AsSpan(index, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

	private static bool TryParseFunctional(string value, out RgbaColor color)
	{
		color = default;
		var match = FunctionalPattern().Match(value);
		if (!match.Success)
		{
			return false;
		}

		var isRgba = match.Groups["fn"].Value.Length == 4;
		if (isRgba != match.Groups["a"].Success)
		{
			return false;
		}

		if (!TryChannel(match.Groups["r"].Value, out var r) ||
			!TryChannel(match.Groups["g"].Value, out var g) ||
			!TryChannel(match.Groups["b"].Value, out var b))
		{
			return false;
		}

		var a = (byte)255;
		if (isRgba)
		{
			if (!double.TryParse(match.Groups["a"].Value, NumberStyles.Float, CultureInfo.InvariantCulture,
					out var alpha) ||
				alpha is < 0 or > 1)
			{
				return false;
			}

			a = ToByte(alpha);
		}

		color = new RgbaColor(r, g, b, a);
		return true;
	}

	private static bool TryChannel(string text, out byte channel)
		=> byte.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out channel);

	[GeneratedRegex(@"\A(?<fn>rgba?)\(\s*(?<r>\d{1,3})\s*,\s*(?<g>\d{1,3})\s*,\s*(?<b>\d{1,3})\s*(?:,\s*(?<a>\d*\.?\d+)\s*)?\)\z",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
	private static partial Regex FunctionalPattern();
}
