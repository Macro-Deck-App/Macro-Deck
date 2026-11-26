using MacroDeckHost.Rendering.Tree;

namespace MacroDeckHost.Rendering.Nodes;

internal static class BarComponents
{
	public static readonly ComponentDefinition RangeBar = new(Components.RangeBar,
		"div",
		(node, scope) => PaintBar(node, scope, false),
		1,
		(node, metrics) => metrics.Horizontal
			? 0
			: UiLength.Resolve(UiLength.Of(node, "thickness"), metrics.Scope) ?? 0);

	public static readonly ComponentDefinition ProgressBar = new(Components.ProgressBar,
		"div",
		(node, scope) => PaintBar(node, scope, true),
		1,
		null,
		false,
		null,
		node => Progress.Of(node, "value") is { Rate: not 0 } ? 1000 : null);

	public static readonly ComponentDefinition Slider = new(Components.Slider,
		"div",
		PaintSlider,
		1,
		(node, metrics) => metrics.Horizontal
			? 0
			: UiLength.Resolve(UiLength.Of(node, "thickness"), metrics.Scope) ?? 0);

	private static void PaintBar(UiNode node, NodeScope scope, bool isProgress)
	{
		var element = scope.Element;
		element.SetClassName("widget-range-bar");
		NodeScope.SizeTo(element, scope.Box);
		var thickness = Layouts.Length(node, "thickness", scope.Scope);
		var track = scope.Part("barTrack", "div");
		track.SetClassName("widget-range-bar-track");
		track.SetStyle("height", NodeScope.Px(thickness));
		track.SetStyle("border-radius", NodeScope.Px(thickness / 2));
		var start = UiLength.HexColor(node, "startColor");
		var end = UiLength.HexColor(node, "endColor");
		var fillStyle = isProgress
			? $"linear-gradient(to right, {start ?? "var(--color-accent)"}, {end ?? "var(--color-accent)"})"
			: start is not null && end is not null
				? $"linear-gradient(to right, {start}, {end})"
				: null;
		if (fillStyle is not null)
		{
			var fill = scope.Part("barFill", "div", track);
			fill.SetClassName(isProgress ? "widget-range-bar-fill widget-progress-fill" : "widget-range-bar-fill");
			var reference = isProgress ? Progress.Of(node, "value") : null;
			var left = isProgress ? 0 : Layouts.ClampUnit(node.Number("start") ?? 0);
			var width = isProgress
				? reference is null ? 0 : Progress.Fraction(reference, scope.Host.NowMs)
				: Math.Max(0,
					Layouts.ClampUnit(node.Number("end") ?? 1) - Layouts.ClampUnit(node.Number("start") ?? 0));
			fill.SetStyle("left", $"{NodeScope.Num(left * 100)}%");
			fill.SetStyle("width", $"{NodeScope.Num(width * 100)}%");
			fill.SetStyle("border-radius", NodeScope.Px(thickness / 2));
			fill.SetStyle("background", fillStyle);
		}

		if (!isProgress && node.Number("marker") is not null && end is not null)
		{
			var marker = scope.Part("barMarker", "div");
			marker.SetClassName("widget-range-bar-marker");
			var stroke = 0.28 * thickness;
			var diameter = Math.Max(0, 1.5 * thickness - stroke);
			marker.SetStyle("left", NodeScope.Px(MarkerLeft(node, scope.Box.Width ?? 0, thickness)));
			marker.SetStyle("width", NodeScope.Px(diameter));
			marker.SetStyle("height", NodeScope.Px(diameter));
			marker.SetStyle("border", $"{NodeScope.Num(stroke)}px solid var(--color-bg-secondary)");
			marker.SetStyle("background", "var(--color-text-primary)");
		}
	}

	private static double MarkerLeft(UiNode node, double width, double thickness)
	{
		var radius = 0.75 * thickness;
		var inset = radius + 0.28 * thickness / 2;
		if (width < 2 * inset)
		{
			return width / 2;
		}

		var marker = Layouts.ClampUnit(node.Number("marker") ?? 0);
		return Math.Min(Math.Max(marker * width, inset), width - inset);
	}

	private static void PaintSlider(UiNode node, NodeScope scope)
	{
		var element = scope.Element;
		var vertical = node.String("direction") == "vertical";
		element.SetClassName("widget-slider");
		element.SetClass("widget-slider-vertical", vertical);
		element.SetClass("widget-pressable", Modifiers.ClaimsValue(node) && !scope.IsDisabled);
		NodeScope.SizeTo(element, scope.Box);
		var thickness = Layouts.Length(node, "thickness", scope.Scope);
		var track = scope.Part("sliderTrack", "div");
		track.SetClassName("widget-slider-track");
		track.SetStyle(vertical ? "width" : "height", NodeScope.Px(thickness));
		var inset = NodeScope.Px(0.28 * thickness);
		track.SetStyle(vertical ? "top" : "left", inset);
		track.SetStyle(vertical ? "bottom" : "right", inset);
		track.SetStyle("border-radius", NodeScope.Px(thickness / 2));
		var fill = scope.Part("sliderFill", "div", track);
		fill.SetClassName("widget-slider-fill");
		fill.SetStyle("border-radius", NodeScope.Px(thickness / 2));
		fill.SetStyle("background", Layouts.LevelColor(node));
		var thumb = scope.Part("sliderThumb", "div");
		thumb.SetClassName("widget-slider-thumb");
		var stroke = 0.28 * thickness;
		var diameter = Math.Max(0, 2.5 * thickness - stroke);
		thumb.SetStyle("width", NodeScope.Px(diameter));
		thumb.SetStyle("height", NodeScope.Px(diameter));
		thumb.SetStyle("border", $"{NodeScope.Num(stroke)}px solid var(--color-bg-secondary)");
		var extent = (vertical ? scope.Box.Height : scope.Box.Width) ?? 0;
		var level = Layouts.Level(node);
		fill.SetStyle(vertical ? "height" : "width", $"{NodeScope.Num(level * 100)}%");
		thumb.SetStyle(vertical ? "bottom" : "left", NodeScope.Px(ThumbOffset(level, extent, thickness)));
	}

	private static double ThumbOffset(double level, double extent, double thickness)
	{
		var stroke = 0.28 * thickness;
		var inset = 1.25 * thickness + stroke / 2;
		var centre = extent < 2 * inset ? extent / 2 : Math.Min(Math.Max(level * extent, inset), extent - inset);
		return centre - Math.Max(0, 2.5 * thickness - stroke) / 2 - stroke;
	}
}
