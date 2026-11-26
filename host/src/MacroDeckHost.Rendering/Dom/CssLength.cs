using System.Globalization;

namespace MacroDeckHost.Rendering.Dom;

internal enum CssLengthKind
{
	Auto,
	None,
	Value,
	MaxContent
}

internal readonly record struct CssLength(CssLengthKind Kind, double Px, double Percent, double Em, double Ch)
{
	public static readonly CssLength Auto = new(CssLengthKind.Auto, 0, 0, 0, 0);

	public static readonly CssLength None = new(CssLengthKind.None, 0, 0, 0, 0);

	public static readonly CssLength Zero = new(CssLengthKind.Value, 0, 0, 0, 0);

	public bool IsAuto => Kind == CssLengthKind.Auto;

	public bool IsValue => Kind == CssLengthKind.Value;

	public bool HasPercent => Kind == CssLengthKind.Value && Percent != 0;

	public static CssLength FromPx(double px)
	{
		return new CssLength(CssLengthKind.Value, px, 0, 0, 0);
	}

	public double Resolve(double percentBase, double fontSize, double zeroWidth)
	{
		return Px + Percent / 100 * percentBase + Em * fontSize + Ch * zeroWidth;
	}

	public double ResolveWithoutPercent(double fontSize, double zeroWidth)
	{
		return Px + Em * fontSize + Ch * zeroWidth;
	}

	public static CssLength Parse(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return Auto;
		}

		var value = text.Trim();
		switch (value)
		{
			case "auto":
				return Auto;
			case "none":
				return None;
			case "max-content":
				return new CssLength(CssLengthKind.MaxContent, 0, 0, 0, 0);
		}

		if (value.StartsWith("calc(", StringComparison.Ordinal) && value.EndsWith(')'))
		{
			value = value[5..^1];
		}

		double px = 0;
		double percent = 0;
		double em = 0;
		double ch = 0;
		var sign = 1;
		var index = 0;
		var parsedAny = false;
		while (index < value.Length)
		{
			var current = value[index];
			if (char.IsWhiteSpace(current))
			{
				index++;
				continue;
			}

			if ((current == '+' || current == '-')
				&& (index + 1 >= value.Length || char.IsWhiteSpace(value[index + 1])))
			{
				sign = current == '-' ? -1 : 1;
				index++;
				continue;
			}

			var start = index;
			if (value[index] is '-' or '+')
			{
				index++;
			}

			while (index < value.Length && (char.IsDigit(value[index]) || value[index] == '.'))
			{
				index++;
			}

			if (index + 1 < value.Length
				&& value[index] is 'e' or 'E'
				&& (char.IsDigit(value[index + 1]) || value[index + 1] == '-'))
			{
				index += 2;
				while (index < value.Length && char.IsDigit(value[index]))
				{
					index++;
				}
			}

			if (!double.TryParse(value[start..index], NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
			{
				return Auto;
			}

			var unitStart = index;
			while (index < value.Length && (char.IsLetter(value[index]) || value[index] == '%'))
			{
				index++;
			}

			var amount = sign * number;
			switch (value[unitStart..index])
			{
				case "px":
				case "":
					px += amount;
					break;
				case "%":
					percent += amount;
					break;
				case "em":
					em += amount;
					break;
				case "ch":
					ch += amount;
					break;
				case "rem":
					px += amount * 16;
					break;
				default:
					return Auto;
			}

			sign = 1;
			parsedAny = true;
		}

		return parsedAny ? new CssLength(CssLengthKind.Value, px, percent, em, ch) : Auto;
	}
}
