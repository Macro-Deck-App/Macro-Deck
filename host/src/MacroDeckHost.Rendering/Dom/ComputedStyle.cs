using System.Globalization;

namespace MacroDeckHost.Rendering.Dom;

internal sealed class ComputedStyle
{
	private readonly Dictionary<string, string> _values;

	private readonly Dictionary<string, CssLength> _lengths = new(StringComparer.Ordinal);

	public ComputedStyle(Dictionary<string, string> values, double fontSize, double zeroWidth)
	{
		_values = values;
		FontSize = fontSize;
		ZeroWidth = zeroWidth;
	}

	public static ComputedStyle Empty()
	{
		return new ComputedStyle(new Dictionary<string, string>(StringComparer.Ordinal), 16, 8);
	}

	public IReadOnlyDictionary<string, string> Values => _values;

	public double FontSize { get; }

	public double ZeroWidth { get; set; }

	public string Get(string name, string fallback = "")
	{
		return _values.TryGetValue(name, out var value) ? value : fallback;
	}

	public bool Has(string name)
	{
		return _values.ContainsKey(name);
	}

	public CssLength Length(string name)
	{
		if (!_lengths.TryGetValue(name, out var length))
		{
			length = CssLength.Parse(Get(name));
			_lengths[name] = length;
		}

		return length;
	}

	public double Px(string name, double percentBase = 0)
	{
		var length = Length(name);
		return length.IsValue ? length.Resolve(percentBase, FontSize, ZeroWidth) : 0;
	}

	public double Number(string name, double fallback)
	{
		return double.TryParse(Get(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
			? value
			: fallback;
	}

	public string Display => Get("display", "inline");

	public string Position => Get("position", "static");

	public bool IsAbsolute => Position is "absolute" or "fixed";

	public bool IsFlex => Display is "flex" or "inline-flex";

	public bool IsColumn => Get("flex-direction", "row") is "column" or "column-reverse";

	public bool IsHidden => Get("visibility") == "hidden";

	public string OverflowX => Get("overflow-x", "visible");

	public string OverflowY => Get("overflow-y", "visible");

	public bool ClipsOverflow => OverflowX != "visible" || OverflowY != "visible";

	public bool ScrollsY => OverflowY is "auto" or "scroll";

	public bool ScrollsX => OverflowX is "auto" or "scroll";

	public double FlexGrow => Number("flex-grow", 0);

	public double FlexShrink => Number("flex-shrink", 1);

	public string WhiteSpace => Get("white-space", "normal");

	// The used line height in px before Chrome rounds it to a layout unit.
	public double LineHeightPx
	{
		get
		{
			var value = Get("line-height", "normal");
			if (value == "normal")
			{
				return FontSize * 1.2;
			}

			if (value.EndsWith("px", StringComparison.Ordinal))
			{
				return CssLength.Parse(value).Px;
			}

			return Number("line-height", 1.2) * FontSize;
		}
	}

	public int LineClamp => (int)Number("-webkit-line-clamp", 0);

	public bool BorderBox => Get("box-sizing", "content-box") == "border-box";
}
