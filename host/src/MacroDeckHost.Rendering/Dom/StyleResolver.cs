using System.Globalization;

namespace MacroDeckHost.Rendering.Dom;

internal sealed class StyleResolver
{
	private static readonly HashSet<string> _inherited = new(StringComparer.Ordinal)
	{
		"color", "font-size", "font-weight", "font-family", "font-style", "line-height", "text-align",
		"white-space", "overflow-wrap", "visibility", "text-shadow", "font-variant-numeric",
		"font-feature-settings", "letter-spacing", "direction", "-webkit-text-fill-color"
	};

	private readonly StyleSheet _sheet;

	private readonly IReadOnlyDictionary<string, string> _tokens;

	private readonly Func<ComputedStyle, double> _zeroWidth;

	public StyleResolver(
		StyleSheet sheet,
		IReadOnlyDictionary<string, string> tokens,
		Func<ComputedStyle, double> zeroWidth)
	{
		_sheet = sheet;
		_tokens = tokens;
		_zeroWidth = zeroWidth;
	}

	public void ResolveTree(Element root)
	{
		Resolve(root, null);
	}

	private void Resolve(Element element, ComputedStyle? parent)
	{
		element.Computed = Compute(element, parent);
		foreach (var child in element.Children)
		{
			Resolve(child, element.Computed);
		}
	}

	private ComputedStyle Compute(Element element, ComputedStyle? parent)
	{
		var values = new Dictionary<string, string>(StringComparer.Ordinal);
		if (parent is not null)
		{
			foreach (var (name, value) in parent.Values)
			{
				if (_inherited.Contains(name) || name.StartsWith("--", StringComparison.Ordinal))
				{
					values[name] = value;
				}
			}
		}
		else
		{
			foreach (var (name, value) in _tokens)
			{
				values[name] = value;
			}
		}

		var parentFontSize = parent?.FontSize ?? 16;
		if (element.IsText)
		{
			return new ComputedStyle(values, parentFontSize, parent?.ZeroWidth ?? 8);
		}

		values["display"] = DefaultDisplay(element.Tag);
		var declared = new List<(int Weight, int Order, string Name, string Value)>();
		foreach (var rule in _sheet.Rules)
		{
			if (!rule.Selector.Matches(element))
			{
				continue;
			}

			foreach (var declaration in rule.Declarations)
			{
				var weight = declaration.Important ? 2_000_000 + rule.Selector.Specificity : rule.Selector.Specificity;
				declared.Add((weight, rule.Order, declaration.Name, declaration.Value));
			}
		}

		var order = _sheet.Rules.Count;
		foreach (var (name, value) in element.Style)
		{
			declared.Add((1_000_000, order++, name, value));
		}

		foreach (var (_, _, name, value) in declared.OrderBy(item => item.Weight).ThenBy(item => item.Order))
		{
			foreach (var (longhand, longValue) in Expand(name, value))
			{
				values[longhand] = longValue;
			}
		}

		foreach (var name in values.Keys.ToList())
		{
			var value = values[name];
			if (value.Contains("var(", StringComparison.Ordinal))
			{
				values[name] = Substitute(value, values, 0);
			}
			else if (value == "inherit")
			{
				values[name] = parent is not null && parent.Values.TryGetValue(name, out var inheritedValue)
					? inheritedValue
					: "";
			}
		}

		// Absolutely positioned boxes and flex items are blockified.
		var position = values.GetValueOrDefault("position");
		var parentDisplay = parent?.Display ?? "block";
		if (position is "absolute" or "fixed" || parentDisplay is "flex" or "inline-flex")
		{
			values["display"] = values["display"] switch
			{
				"inline" or "inline-block" => "block",
				"inline-flex" => "flex",
				var display => display
			};
		}

		var fontSize = ComputeFontSize(values.GetValueOrDefault("font-size"), parentFontSize);
		values["font-size"] = string.Create(CultureInfo.InvariantCulture, $"{fontSize}px");
		var style = new ComputedStyle(values, fontSize, 8);
		style.ZeroWidth = _zeroWidth(style);
		return style;
	}

	private static double ComputeFontSize(string? value, double parentFontSize)
	{
		if (string.IsNullOrEmpty(value))
		{
			return parentFontSize;
		}

		var length = CssLength.Parse(value);
		if (!length.IsValue)
		{
			return parentFontSize;
		}

		return length.Px + length.Em * parentFontSize + length.Percent / 100 * parentFontSize;
	}

	private string Substitute(string value, Dictionary<string, string> values, int depth)
	{
		if (depth > 8)
		{
			return value;
		}

		var start = value.IndexOf("var(", StringComparison.Ordinal);
		while (start >= 0)
		{
			var depthCount = 0;
			var end = start + 3;
			for (; end < value.Length; end++)
			{
				if (value[end] == '(')
				{
					depthCount++;
				}
				else if (value[end] == ')')
				{
					depthCount--;
					if (depthCount == 0)
					{
						break;
					}
				}
			}

			var inner = value[(start + 4)..end];
			var comma = inner.IndexOf(',', StringComparison.Ordinal);
			var name = (comma < 0 ? inner : inner[..comma]).Trim();
			var fallback = comma < 0 ? "" : inner[(comma + 1)..].Trim();
			var resolved = values.TryGetValue(name, out var found) ? found : _tokens.GetValueOrDefault(name, fallback);
			resolved = Substitute(resolved, values, depth + 1);
			value = value[..start] + resolved + value[(end + 1)..];
			start = value.IndexOf("var(", StringComparison.Ordinal);
		}

		return value;
	}

	private static string DefaultDisplay(string tag)
	{
		return tag switch
		{
			"div" or "html" or "body" or "p" => "block",
			"input" or "button" => "inline-block",
			_ => "inline"
		};
	}

	private static IEnumerable<(string, string)> Expand(string name, string value)
	{
		switch (name)
		{
			case "padding":
			case "margin":
				return Sides(value).Select((side, index) => ($"{name}-{_sideNames[index]}", side));
			case "inset":
				return Sides(value).Select((side, index) => (_sideNames[index], side));
			case "overflow":
			{
				var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
				return [("overflow-x", parts[0]), ("overflow-y", parts.Length > 1 ? parts[1] : parts[0])];
			}

			case "gap":
			{
				var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
				return [("row-gap", parts[0]), ("column-gap", parts.Length > 1 ? parts[1] : parts[0])];
			}

			case "flex":
				return ExpandFlex(value);
			case "border":
				return ExpandBorder(value);
			case "border-width":
				return Sides(value).Select((side, index) => ($"border-{_sideNames[index]}-width", side));
			case "border-style":
				return _sideNames.Select(side => ($"border-{side}-style", value));
			case "border-color":
				return _sideNames.Select(side => ($"border-{side}-color", value));
			case "background":
				return
				[
					("background", value),
					("background-color", value.Contains("gradient", StringComparison.Ordinal) ? "transparent" : value)
				];
			case "-webkit-transform":
				return [("transform", value)];
			case "-webkit-transform-origin":
				return [("transform-origin", value)];
			case "-webkit-clip-path":
				return [("clip-path", value)];
			case "-webkit-mask-image":
				return [("mask-image", value)];
			case "transition":
			case "animation":
				return [];
			default:
				return [(name, value)];
		}
	}

	private static readonly string[] _sideNames = ["top", "right", "bottom", "left"];

	private static string[] Sides(string value)
	{
		var parts = StyleSheet.SplitTopLevel(value, ' ')
			.Select(part => part.Trim())
			.Where(part => part.Length > 0)
			.ToArray();
		return parts.Length switch
		{
			1 => [parts[0], parts[0], parts[0], parts[0]],
			2 => [parts[0], parts[1], parts[0], parts[1]],
			3 => [parts[0], parts[1], parts[2], parts[1]],
			_ => [parts[0], parts[1], parts[2], parts[3]]
		};
	}

	private static IEnumerable<(string, string)> ExpandFlex(string value)
	{
		if (value == "none")
		{
			return [("flex-grow", "0"), ("flex-shrink", "0"), ("flex-basis", "auto")];
		}

		var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		var grow = parts.Length > 0 ? parts[0] : "0";
		var shrink = parts.Length > 1 ? parts[1] : "1";
		var basis = parts.Length > 2 ? parts[2] : "0%";
		return [("flex-grow", grow), ("flex-shrink", shrink), ("flex-basis", basis)];
	}

	private static IEnumerable<(string, string)> ExpandBorder(string value)
	{
		var parts = value.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
		var width = parts.Length > 0 ? parts[0] : "0";
		var style = parts.Length > 1 ? parts[1] : "none";
		var color = parts.Length > 2 ? parts[2] : "currentColor";
		foreach (var side in _sideNames)
		{
			yield return ($"border-{side}-width", width);
			yield return ($"border-{side}-style", style);
			yield return ($"border-{side}-color", color);
		}
	}
}
