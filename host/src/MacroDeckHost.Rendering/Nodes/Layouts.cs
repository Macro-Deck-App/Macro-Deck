using MacroDeckHost.Rendering.Tree;

namespace MacroDeckHost.Rendering.Nodes;

internal static class Layouts
{
	public static double IntrinsicMainPx(UiNode node, IntrinsicMetrics metrics)
	{
		if (UiLength.Resolve(UiLength.Of(node, "mainSize"), metrics.Scope) is { } declared)
		{
			return declared;
		}

		return Components.Get(node.Type)?.Intrinsic?.Invoke(node, metrics) ?? 0;
	}

	public static List<ChildEntry> StackChildren(
		UiNode node,
		BoxSize box,
		double basis,
		double padding,
		double gap,
		bool horizontal,
		bool natural)
	{
		double? Inner(double? outer)
		{
			return outer is { } value ? Math.Max(0, value - 2 * padding) : null;
		}

		var contentWidth = Inner(box.Width);
		var contentHeight = Inner(box.Height);
		var mainTotal = horizontal ? contentWidth : contentHeight;
		var crossTotal = horizontal ? contentHeight : contentWidth;
		var container = UiLength.BoxExtent(contentWidth, contentHeight);
		var metrics = new IntrinsicMetrics(basis, crossTotal, container, horizontal);
		var specs = node.Children.Select(child =>
			{
				var mainSize = UiLength.Resolve(UiLength.Of(child, "mainSize"), metrics.Scope);
				var fill = !natural && mainSize is null && child.Boolean("fill") == true;
				var intrinsic = mainSize is null && !fill ? IntrinsicMainPx(child, metrics) : 0;
				return (Child: child, MainSize: mainSize, Fill: fill, Intrinsic: intrinsic);
			})
			.ToList();
		var gapsTotal = Math.Max(0, node.Children.Count - 1) * gap;
		var fixedTotal = specs.Sum(spec => spec.MainSize ?? spec.Intrinsic);
		var fillCount = specs.Count(spec => spec.Fill);
		double? fillShare = mainTotal is null || fillCount == 0
			? null
			: Math.Max(0, mainTotal.Value - gapsTotal - fixedTotal) / fillCount;
		return specs.Select(spec =>
			{
				var main = spec.MainSize ?? (spec.Fill ? fillShare : null);
				var childBox = horizontal ? new BoxSize(main, crossTotal) : new BoxSize(crossTotal, main);
				return new ChildEntry(spec.Child, childBox, crossTotal, true, container);
			})
			.ToList();
	}

	public static bool IsHorizontal(UiNode node)
	{
		return node.String("direction") == "horizontal";
	}

	public static string Justify(UiNode node)
	{
		return node.String("justify") switch
		{
			"center" => "center",
			"end" => "flex-end",
			"space-between" => "space-between",
			_ => "flex-start"
		};
	}

	public static string Align(UiNode node)
	{
		return node.String("align") switch
		{
			"start" => "flex-start",
			"center" => "center",
			"end" => "flex-end",
			"baseline" => "baseline",
			_ => "stretch"
		};
	}

	public static double Length(UiNode node, string key, LengthScope scope)
	{
		return UiLength.Resolve(UiLength.Of(node, key), scope) ?? 0;
	}

	public static double ClampUnit(double value)
	{
		return Math.Min(1, Math.Max(0, value));
	}

	public static double FontWeight(string? weight)
	{
		return weight switch
		{
			"medium" => 500,
			"semibold" => 600,
			"bold" => 700,
			_ => 400
		};
	}

	public static string RoleColor(UiNode node)
	{
		return node.String("role") switch
		{
			"secondary" => "var(--color-text-secondary)",
			"muted" => "var(--color-text-muted)",
			_ => "var(--color-text-primary)"
		};
	}

	public static string FillColor(UiNode node)
	{
		return UiLength.HexColor(node, "color") ?? RoleColor(node);
	}

	public static string TextAlign(UiNode node)
	{
		return node.String("align") switch
		{
			"center" => "center",
			"end" => "end",
			_ => "start"
		};
	}

	public static bool Wraps(UiNode node)
	{
		return node.Boolean("wrap") == true;
	}

	public static int MaxLines(UiNode node)
	{
		if (node.Number("maxLines") is { } lines && lines >= 1)
		{
			return (int)Math.Floor(lines);
		}

		return Wraps(node) ? 0 : 1;
	}

	public static double? Digits(UiNode node)
	{
		return node.Number("digits") is > 0 and var digits ? digits : null;
	}

	public static string Fit(UiNode node)
	{
		return node.String("fit") == "cover" ? "cover" : "contain";
	}

	public static double Zoom(UiNode node)
	{
		return node.Number("zoom") is > 0 and var zoom ? zoom : 1;
	}

	public static double Opacity(UiNode node)
	{
		return node.Number("opacity") is { } opacity ? ClampUnit(opacity) : 1;
	}

	public static string ArtworkTransform(UiNode node)
	{
		var x = (node.Number("offsetX") ?? 0) * 100;
		var y = (node.Number("offsetY") ?? 0) * 100;
		return $"translate({NodeScope.Num(x)}%, {NodeScope.Num(y)}%) scale({NodeScope.Num(Zoom(node))})";
	}

	public static string? ArtworkFilter(UiNode node)
	{
		var brightness = node.Number("brightness");
		var saturation = node.Number("saturation");
		if (brightness is null && saturation is null)
		{
			return null;
		}

		var parts = new List<string>();
		if (brightness is { } b)
		{
			parts.Add($"brightness({NodeScope.Num(Math.Min(2, Math.Max(0, b)))})");
		}

		if (saturation is { } s)
		{
			parts.Add($"saturate({NodeScope.Num(Math.Min(2, Math.Max(0, s)))})");
		}

		return string.Join(' ', parts);
	}

	public static string LevelColor(UiNode node)
	{
		return UiLength.HexColor(node, "levelColor") ?? "var(--color-accent)";
	}

	public static double Level(UiNode node)
	{
		return ClampUnit(node.Number("level") ?? 0);
	}
}
