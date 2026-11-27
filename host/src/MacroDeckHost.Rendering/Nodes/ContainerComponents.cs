using System.Text.Json;
using MacroDeckHost.Rendering.Tree;

namespace MacroDeckHost.Rendering.Nodes;

internal static class ContainerComponents
{
	public static readonly ComponentDefinition Layer = new(Components.Layer, "div", PaintLayer, 1, WidestChild);

	public static readonly ComponentDefinition Transform =
		new(Components.Transform, "div", PaintTransform, 1, WidestChild);

	public static readonly ComponentDefinition Modifier = new(Components.Modifier,
		"div",
		PaintModifier,
		1,
		ModifierIntrinsic,
		false,
		ModifierInputsOf);

	public static readonly ComponentDefinition Responsive = new(Components.Responsive,
		"div",
		PaintResponsive,
		1,
		ResponsiveIntrinsic,
		true);

	public static readonly ComponentDefinition FirstFit = new(Components.FirstFit,
		"div",
		PaintFirstFit,
		1,
		(node, metrics) => node.Children.Count == 0 ? 0 : metrics.Nested().OfChild(node.Children[^1]),
		true);

	private static double WidestChild(UiNode node, IntrinsicMetrics metrics)
	{
		return node.Children.Aggregate(0.0, (widest, child) => Math.Max(widest, metrics.Nested().OfChild(child)));
	}

	private static void PaintLayer(UiNode node, NodeScope scope)
	{
		scope.Element.SetClassName("widget-layer");
		NodeScope.SizeTo(scope.Element, scope.Box);
		scope.SyncChildren(scope.Element, node.Children.Select(child => new ChildEntry(child, scope.Box, null)));
		scope.PressTint();
	}

	private static void PaintTransform(UiNode node, NodeScope scope)
	{
		var element = scope.Element;
		element.SetClassName("widget-transform");
		NodeScope.SizeTo(element, scope.Box);
		var rotation = node.Number("rotation") ?? 0;
		var zoom = node.Number("zoom") is > 0 and var requested ? requested : 1;
		var offsetX = node.Number("offsetX") ?? 0;
		var offsetY = node.Number("offsetY") ?? 0;
		if (rotation != 0 || zoom != 1 || offsetX != 0 || offsetY != 0)
		{
			var originX = node.Number("originX") ?? 0.5;
			var originY = node.Number("originY") ?? 0.5;
			element.SetStyle("transform",
				$"translate({NodeScope.Num(offsetX * 100)}%, {NodeScope.Num(offsetY * 100)}%)"
				+ $" rotate({NodeScope.Num(rotation)}deg) scale({NodeScope.Num(zoom)})");
			element.SetStyle("transform-origin", $"{NodeScope.Num(originX * 100)}% {NodeScope.Num(originY * 100)}%");
		}

		scope.SyncChildren(element, node.Children.Select(child => new ChildEntry(child, scope.Box, null)));
		scope.PressTint();
	}

	private sealed record Frame(BoxSize Box, double? MinWidth, double? MaxWidth, double? MinHeight, double? MaxHeight);

	private static Frame ResolveFrame(UiNode node, BoxSize box, LengthScope scope)
	{
		var frame = node.Record("frame");

		double? Length(string key)
		{
			return frame is { } record && record.TryGetProperty(key, out var value)
				? UiLength.Resolve(UiLength.From(value), scope)
				: null;
		}

		double? Clamp(double? value, double? min, double? max)
		{
			if (value is not { } clamped)
			{
				return null;
			}

			if (max is { } maximum)
			{
				clamped = Math.Min(clamped, maximum);
			}

			if (min is { } minimum)
			{
				clamped = Math.Max(clamped, minimum);
			}

			return clamped;
		}

		var minWidth = Length("minWidth");
		var maxWidth = Length("maxWidth");
		var minHeight = Length("minHeight");
		var maxHeight = Length("maxHeight");
		var width = Clamp(Length("width") ?? box.Width, minWidth, maxWidth);
		var height = Clamp(Length("height") ?? box.Height, minHeight, maxHeight);
		if (frame is { } values
			&& values.TryGetProperty("aspectRatio", out var ratioValue)
			&& ratioValue.ValueKind == JsonValueKind.Number
			&& ratioValue.GetDouble() is > 0 and var ratio
			&& double.IsFinite(ratio))
		{
			if (width is not null && height is null)
			{
				height = Clamp(width / ratio, minHeight, maxHeight);
			}
			else if (height is not null && width is null)
			{
				width = Clamp(height * ratio, minWidth, maxWidth);
			}
			else if (width is not null && height is not null)
			{
				if (width / height > ratio)
				{
					width = height * ratio;
				}
				else
				{
					height = width / ratio;
				}
			}
		}

		return new Frame(new BoxSize(width, height), minWidth, maxWidth, minHeight, maxHeight);
	}

	private static ModifierInputs ModifierInputsOf(UiNode node)
	{
		var opacity = node.Number("opacity") is { } value ? Math.Min(1, Math.Max(0, value)) : (double?)null;
		return new ModifierInputs(opacity, node.String("clip") == "capsule" ? "9999px" : null);
	}

	private static void PaintModifier(UiNode node, NodeScope scope)
	{
		var element = scope.Element;
		var frame = ResolveFrame(node, scope.Box, scope.Scope);
		var face = frame.Box;
		element.SetClassName("widget-modifier"
			+ (face.Width is null ? " widget-modifier-fill-width" : "")
			+ (face.Height is null ? " widget-modifier-fill-height" : ""));
		NodeScope.SizeTo(element, face);
		element.SetStyle("min-width", face.Width is null ? NodeScope.Px(frame.MinWidth) : null);
		element.SetStyle("max-width", face.Width is null ? NodeScope.Px(frame.MaxWidth) : null);
		element.SetStyle("min-height", face.Height is null ? NodeScope.Px(frame.MinHeight) : null);
		element.SetStyle("max-height", face.Height is null ? NodeScope.Px(frame.MaxHeight) : null);
		var padding = Layouts.Length(node, "padding", scope.Scope);
		element.SetStyle("padding", padding > 0 ? NodeScope.Px(padding) : null);
		var clip = node.String("clip");
		element.SetStyle("overflow", clip is "bounds" or "capsule" ? "hidden" : null);
		element.SetStyle("clip-path", clip == "circle" ? "circle(50% at 50% 50%)" : null);
		var mask = Modifiers.GradientCss(node.Raw("mask"),
			stop => stop.TryGetProperty("opacity", out var opacity)
				&& opacity.ValueKind == JsonValueKind.Number
				&& opacity.GetDouble() is >= 0 and <= 1
					? $"rgba(0, 0, 0, {NodeScope.Num(opacity.GetDouble())})"
					: null);
		element.SetStyle("mask-image", mask);

		double? Inner(double? outer)
		{
			return outer is { } value ? Math.Max(0, value - 2 * padding) : null;
		}

		if (node.Children.Count > 0)
		{
			scope.SyncChildren(element,
			[
				new ChildEntry(node.Children[0],
					new BoxSize(Inner(face.Width), Inner(face.Height)),
					Inner(scope.CrossExtent))
			]);
		}

		scope.PressTint();
	}

	private static double ModifierIntrinsic(UiNode node, IntrinsicMetrics metrics)
	{
		var box = metrics.Horizontal ? new BoxSize(null, metrics.CrossExtent) : new BoxSize(metrics.CrossExtent, null);
		var frame = ResolveFrame(node, box, metrics.Scope);
		var fixedMain = metrics.Horizontal ? frame.Box.Width : frame.Box.Height;
		if (fixedMain is { } main)
		{
			return main;
		}

		var size = (node.Children.Count == 0 ? 0 : metrics.Nested().OfChild(node.Children[0]))
			+ 2 * Layouts.Length(node, "padding", metrics.Scope);
		if ((metrics.Horizontal ? frame.MaxWidth : frame.MaxHeight) is { } max)
		{
			size = Math.Min(size, max);
		}

		if ((metrics.Horizontal ? frame.MinWidth : frame.MinHeight) is { } min)
		{
			size = Math.Max(size, min);
		}

		return size;
	}

	public static UiNode? ResponsiveChild(UiNode node, BoxSize? box)
	{
		var index = ResponsiveIndex(node, box);
		return index < 0 || index >= node.Children.Count ? null : node.Children[index];
	}

	private static int ResponsiveIndex(UiNode node, BoxSize? box)
	{
		const double tolerance = 0.0001;
		if (node.Children.Count == 0)
		{
			return -1;
		}

		if (node.Raw("variants") is not { ValueKind: JsonValueKind.Array } variants)
		{
			return 0;
		}

		double? Known(double? extent)
		{
			return extent is { } value && double.IsFinite(value) && value > 0 ? value / UiLength.Cell : null;
		}

		var width = Known(box?.Width);
		var height = Known(box?.Height);
		var aspect = width is not null && height is not null ? width / height : null;
		var index = 0;
		foreach (var condition in variants.EnumerateArray())
		{
			if (index + 1 >= node.Children.Count)
			{
				break;
			}

			if (condition.ValueKind == JsonValueKind.Object)
			{
				bool Min(string name, double? value)
				{
					return !condition.TryGetProperty(name, out var limit)
						|| limit.ValueKind != JsonValueKind.Number
						|| value is { } known && known >= limit.GetDouble() - tolerance;
				}

				bool Max(string name, double? value)
				{
					return !condition.TryGetProperty(name, out var limit)
						|| limit.ValueKind != JsonValueKind.Number
						|| value is { } known && known < limit.GetDouble() - tolerance;
				}

				if (Min("minWidth", width)
					&& Max("maxWidth", width)
					&& Min("minHeight", height)
					&& Max("maxHeight", height)
					&& Min("minAspect", aspect)
					&& Max("maxAspect", aspect))
				{
					return index + 1;
				}
			}

			index++;
		}

		return 0;
	}

	private static void PaintResponsive(UiNode node, NodeScope scope)
	{
		scope.Element.SetClassName("widget-responsive");
		NodeScope.SizeTo(scope.Element, scope.Box);
		if (ResponsiveChild(node, scope.GivenBox) is { } chosen)
		{
			scope.SyncChildren(scope.Element, [new ChildEntry(chosen, scope.Box, null)]);
		}
	}

	private static double ResponsiveIntrinsic(UiNode node, IntrinsicMetrics metrics)
	{
		var box = metrics.Horizontal ? new BoxSize(null, metrics.CrossExtent) : new BoxSize(metrics.CrossExtent, null);
		return ResponsiveChild(node, box) is { } chosen ? metrics.Nested().OfChild(chosen) : 0;
	}

	private static void PaintFirstFit(UiNode node, NodeScope scope)
	{
		var element = scope.Element;
		element.SetClassName("widget-first-fit");
		NodeScope.SizeTo(element, scope.Box);
		scope.SyncChildren(element, node.Children.Select(child => new ChildEntry(child, scope.Box, null)));
		var candidates = element.Children.Where(child => child.NodeId is not null).ToList();
		for (var index = 0; index < candidates.Count; index++)
		{
			var last = index == candidates.Count - 1;
			candidates[index].SetAttribute("data-first-fit-last", last ? "" : null);
			candidates[index].SetAttribute("data-first-fit-hidden", last ? null : "");
		}

		scope.Host.FirstFits.Add(element);
	}
}
