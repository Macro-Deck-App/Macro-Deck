using System.Globalization;
using MacroDeckHost.Rendering.Dom;

namespace MacroDeckHost.Rendering.Layout;

internal readonly record struct Constraint(
	LayoutUnit AvailableWidth,
	LayoutUnit? FixedWidth,
	LayoutUnit? FixedHeight,
	LayoutUnit? PercentWidth,
	LayoutUnit? PercentHeight,
	bool ShrinkToFit);

internal readonly record struct Edges(LayoutUnit Top, LayoutUnit Right, LayoutUnit Bottom, LayoutUnit Left)
{
	public LayoutUnit Horizontal => Left + Right;

	public LayoutUnit Vertical => Top + Bottom;
}

internal sealed partial class LayoutEngine
{
	public const int ScrollbarWidth = 15;

	private readonly InlineLayout _inline;

	public LayoutEngine(InlineLayout inline)
	{
		_inline = inline;
	}

	public InlineLayout Inline => _inline;

	public static bool Displayed(Element element)
	{
		return !element.IsText && element.Computed.Display != "none";
	}

	public static LayoutUnit Length(ComputedStyle style, string name, LayoutUnit? percentBase)
	{
		var length = style.Length(name);
		if (!length.IsValue)
		{
			return LayoutUnit.Zero;
		}

		return LayoutUnit.FromDouble(length.Resolve(percentBase?.Value ?? 0, style.FontSize, style.ZeroWidth));
	}

	public static Edges Padding(ComputedStyle style, LayoutUnit? percentBase)
	{
		return new Edges(Length(style, "padding-top", percentBase),
			Length(style, "padding-right", percentBase),
			Length(style, "padding-bottom", percentBase),
			Length(style, "padding-left", percentBase));
	}

	public static Edges Border(ComputedStyle style)
	{
		LayoutUnit Side(string side)
		{
			var line = style.Get($"border-{side}-style", "none");
			return line is "none" or "hidden" ? LayoutUnit.Zero : Length(style, $"border-{side}-width", null);
		}

		return new Edges(Side("top"), Side("right"), Side("bottom"), Side("left"));
	}

	public static Edges Margin(ComputedStyle style, LayoutUnit? percentBase)
	{
		return new Edges(Length(style, "margin-top", percentBase),
			Length(style, "margin-right", percentBase),
			Length(style, "margin-bottom", percentBase),
			Length(style, "margin-left", percentBase));
	}

	public static bool AutoMargin(ComputedStyle style, string side)
	{
		return style.Get($"margin-{side}") == "auto";
	}

	public static LayoutUnit? SizeProperty(Element element, string name, LayoutUnit? percentBase, bool horizontal)
	{
		var style = element.Computed;
		var length = style.Length(name);
		if (!length.IsValue || length.HasPercent && percentBase is null)
		{
			return null;
		}

		var value = LayoutUnit.FromDouble(length.Resolve(percentBase?.Value ?? 0, style.FontSize, style.ZeroWidth));
		if (!style.BorderBox)
		{
			var padding = Padding(style, null);
			var border = Border(style);
			value += horizontal ? padding.Horizontal + border.Horizontal : padding.Vertical + border.Vertical;
		}

		return value.ClampNegativeToZero();
	}

	public static LayoutUnit ClampWidth(Element element, LayoutUnit width, LayoutUnit? percentBase)
	{
		return Clamp(element, width, "min-width", "max-width", percentBase, true);
	}

	public static LayoutUnit ClampHeight(Element element, LayoutUnit height, LayoutUnit? percentBase)
	{
		return Clamp(element, height, "min-height", "max-height", percentBase, false);
	}

	private static LayoutUnit Clamp(
		Element element,
		LayoutUnit size,
		string min,
		string max,
		LayoutUnit? percentBase,
		bool horizontal)
	{
		if (SizeProperty(element, max, percentBase, horizontal) is { } maximum)
		{
			size = LayoutUnit.Min(size, maximum);
		}

		if (SizeProperty(element, min, percentBase, horizontal) is { } minimum)
		{
			size = LayoutUnit.MaxOf(size, minimum);
		}

		return size;
	}

	public static bool IsReplaced(Element element)
	{
		return element.Tag is "svg" or "img" or "input";
	}

	public static bool IsScrollContainer(ComputedStyle style)
	{
		return style.ClipsOverflow && style.OverflowX != "clip" && style.OverflowY != "clip";
	}

	private static (LayoutUnit Right, LayoutUnit Bottom) Gutters(ComputedStyle style, bool overflowsX, bool overflowsY)
	{
		var right = style.ScrollsY && (style.Get("scrollbar-gutter") == "stable" || overflowsY)
			? LayoutUnit.FromInt(ScrollbarWidth)
			: LayoutUnit.Zero;
		var bottom = style.ScrollsX && overflowsX ? LayoutUnit.FromInt(ScrollbarWidth) : LayoutUnit.Zero;
		return (right, bottom);
	}

	public void Layout(Element element, Constraint constraint)
	{
		var style = element.Computed;
		var box = element.Box;
		box.Displayed = true;
		box.Lines.Clear();
		box.HasBaseline = false;
		var padding = Padding(style, constraint.PercentWidth);
		var border = Border(style);

		var width = constraint.FixedWidth ?? ResolveWidth(element, constraint, padding, border);
		box.Width = width;

		var explicitHeight = constraint.FixedHeight
			?? (SizeProperty(element, "height", constraint.PercentHeight, false) is { } declared
				? ClampHeight(element, declared, constraint.PercentHeight)
				: null);
		var contentWidth = (width - padding.Horizontal - border.Horizontal).ClampNegativeToZero();
		LayoutUnit? contentHeight = explicitHeight is { } fixedHeight
			? (fixedHeight - padding.Vertical - border.Vertical).ClampNegativeToZero()
			: null;

		var (gutterRight, gutterBottom) = Gutters(style, false, false);
		var innerWidth = (contentWidth - gutterRight).ClampNegativeToZero();
		var used = LayoutContent(element, innerWidth, contentHeight, padding, border);
		if (IsScrollContainer(style)
			&& style.ScrollsX
			&& used.ContentWidth > innerWidth
			&& gutterBottom == LayoutUnit.Zero)
		{
			gutterBottom = LayoutUnit.FromInt(ScrollbarWidth);
			if (contentHeight is { } bounded)
			{
				used = LayoutContent(element,
					innerWidth,
					(bounded - gutterBottom).ClampNegativeToZero(),
					padding,
					border);
			}
		}

		box.ScrollbarRight = gutterRight;
		box.ScrollbarBottom = gutterBottom;
		var height = explicitHeight
			?? ClampHeight(element,
				used.ContentHeight + padding.Vertical + border.Vertical + gutterBottom,
				constraint.PercentHeight);
		box.Height = height;
		box.ScrollableWidth = LayoutUnit.MaxOf(width - border.Horizontal - gutterRight,
			used.ContentWidth + padding.Horizontal);
		box.ScrollableHeight = LayoutUnit.MaxOf(height - border.Vertical - gutterBottom,
			used.ContentHeight + padding.Vertical);

		LayoutAbsoluteChildren(element, padding, border);
	}

	private LayoutUnit ResolveWidth(Element element, Constraint constraint, Edges padding, Edges border)
	{
		var style = element.Computed;
		if (SizeProperty(element, "width", constraint.PercentWidth, true) is { } declared)
		{
			return ClampWidth(element, declared, constraint.PercentWidth);
		}

		if (style.Length("width").Kind == CssLengthKind.MaxContent)
		{
			return ClampWidth(element, Intrinsic(element).Max, constraint.PercentWidth);
		}

		if (IsReplaced(element))
		{
			return ClampWidth(element,
				ReplacedWidth(element) ?? padding.Horizontal + border.Horizontal,
				constraint.PercentWidth);
		}

		var margin = Margin(style, constraint.PercentWidth);
		var available = (constraint.AvailableWidth - margin.Horizontal).ClampNegativeToZero();
		if (constraint.ShrinkToFit || style.Display is "inline-block" or "inline-flex" or "inline")
		{
			var (min, max) = Intrinsic(element);
			available = LayoutUnit.Min(max, LayoutUnit.MaxOf(min, available));
		}

		return ClampWidth(element, available, constraint.PercentWidth);
	}

	public static LayoutUnit? ReplacedWidth(Element element)
	{
		return element.Tag == "svg" && element.Attributes.TryGetValue("width", out var width) ? Attribute(width) : null;
	}

	public static LayoutUnit? ReplacedHeight(Element element)
	{
		return element.Tag == "svg" && element.Attributes.TryGetValue("height", out var height)
			? Attribute(height)
			: null;
	}

	private static LayoutUnit? Attribute(string value)
	{
		return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
			? LayoutUnit.FromDouble(parsed)
			: null;
	}

	private readonly record struct ContentSize(LayoutUnit ContentWidth, LayoutUnit ContentHeight);

	private ContentSize LayoutContent(
		Element element,
		LayoutUnit contentWidth,
		LayoutUnit? contentHeight,
		Edges padding,
		Edges border)
	{
		var style = element.Computed;
		var offsetX = padding.Left + border.Left;
		var offsetY = padding.Top + border.Top;
		if (IsReplaced(element))
		{
			var natural = element.Tag switch
			{
				"svg" => ReplacedHeight(element) is { } svgHeight
					? svgHeight - padding.Vertical - border.Vertical
					: LayoutUnit.Zero,
				"input" => InlineLayout.LineHeight(style),
				_ => LayoutUnit.Zero
			};
			return new ContentSize(contentWidth, natural.ClampNegativeToZero());
		}

		if (style.IsFlex)
		{
			return LayoutFlex(element, contentWidth, contentHeight, offsetX, offsetY);
		}

		if (InlineLayout.HasInlineContent(element))
		{
			return LayoutInlineContent(element, contentWidth, offsetX, offsetY);
		}

		var y = LayoutUnit.Zero;
		var widest = LayoutUnit.Zero;
		foreach (var child in element.Children)
		{
			if (!Displayed(child))
			{
				child.Box.Displayed = false;
				continue;
			}

			if (child.Computed.IsAbsolute)
			{
				continue;
			}

			var margin = Margin(child.Computed, contentWidth);
			Layout(child, new Constraint(contentWidth, null, null, contentWidth, contentHeight, false));
			child.Box.X = offsetX + margin.Left;
			child.Box.Y = offsetY + y + margin.Top;
			y += margin.Top + child.Box.Height + margin.Bottom;
			widest = LayoutUnit.MaxOf(widest, margin.Left + child.Box.Width + margin.Right);
		}

		return new ContentSize(widest, y);
	}

	private ContentSize LayoutInlineContent(
		Element element,
		LayoutUnit contentWidth,
		LayoutUnit offsetX,
		LayoutUnit offsetY)
	{
		var style = element.Computed;
		var lines = _inline.BuildLines(element, contentWidth);
		var (ascent, descent) = LineMetrics(element);
		var lineHeight = ascent + descent;
		var clamp = style.Display == "-webkit-box" ? style.LineClamp : 0;
		var shown = clamp > 0 ? Math.Min(clamp, lines.Count) : lines.Count;
		var align = style.Get("text-align", "start");
		var widest = LayoutUnit.Zero;
		for (var index = 0; index < lines.Count; index++)
		{
			var line = lines[index];
			line.Top = offsetY + lineHeight * index;
			line.Height = lineHeight;
			line.Baseline = ascent;
			var free = contentWidth - line.Width;
			line.Offset = offsetX
				+ align switch
				{
					"center" => free.ClampNegativeToZero() / 2,
					"end" or "right" => free.ClampNegativeToZero(),
					_ => LayoutUnit.Zero
				};
			widest = LayoutUnit.MaxOf(widest, line.Width);
			element.Box.Lines.Add(line);
		}

		element.Box.Baseline = offsetY + ascent;
		element.Box.HasBaseline = true;
		return new ContentSize(widest, lineHeight * shown);
	}

	public (LayoutUnit Ascent, LayoutUnit Descent) LineMetrics(Element element)
	{
		var (ascent, descent) = BoxMetrics(element.Computed);
		foreach (var inline in element.Descendants())
		{
			if (inline.IsText || inline.Computed.Display != "inline")
			{
				continue;
			}

			var (spanAscent, spanDescent) = BoxMetrics(inline.Computed);
			ascent = LayoutUnit.MaxOf(ascent, spanAscent);
			descent = LayoutUnit.MaxOf(descent, spanDescent);
		}

		return (ascent, descent);
	}

	// NGLineHeightMetrics::AddLeading: the ascent takes the half-leading floored to a whole pixel.
	private (LayoutUnit Ascent, LayoutUnit Descent) BoxMetrics(ComputedStyle style)
	{
		var lineHeight = InlineLayout.LineHeight(style);
		var face = _inline.FontMetrics(style);
		var halfLeading = (lineHeight - (face.Ascent + face.Descent)) / 2;
		var ascent = face.Ascent + LayoutUnit.FromInt((int)Math.Floor(halfLeading.Value));
		return (ascent, lineHeight - ascent);
	}

	public (LayoutUnit Min, LayoutUnit Max) Intrinsic(Element element)
	{
		var style = element.Computed;
		var padding = Padding(style, null);
		var border = Border(style);
		var edges = padding.Horizontal
			+ border.Horizontal
			+ (style.ScrollsY && style.Get("scrollbar-gutter") == "stable"
				? LayoutUnit.FromInt(ScrollbarWidth)
				: LayoutUnit.Zero);
		if (IsReplaced(element))
		{
			var replaced = ReplacedWidth(element) ?? SizeProperty(element, "width", null, true) ?? edges;
			return (replaced, replaced);
		}

		LayoutUnit min;
		LayoutUnit max;
		if (style.IsFlex)
		{
			(min, max) = FlexIntrinsic(element);
		}
		else if (InlineLayout.HasInlineContent(element))
		{
			min = _inline.MinContent(element);
			max = _inline.MaxContent(element);
		}
		else
		{
			min = LayoutUnit.Zero;
			max = LayoutUnit.Zero;
			foreach (var child in element.Children)
			{
				if (!Displayed(child) || child.Computed.IsAbsolute)
				{
					continue;
				}

				var contribution = Contribution(child);
				min = LayoutUnit.MaxOf(min, contribution.Min);
				max = LayoutUnit.MaxOf(max, contribution.Max);
			}
		}

		if (SizeProperty(element, "min-width", null, true) is { } minimum)
		{
			min = LayoutUnit.MaxOf(min, minimum - edges);
			max = LayoutUnit.MaxOf(max, minimum - edges);
		}

		return (min + edges, max + edges);
	}

	public (LayoutUnit Min, LayoutUnit Max) Contribution(Element child)
	{
		var margin = Margin(child.Computed, null);
		LayoutUnit min;
		LayoutUnit max;
		if (SizeProperty(child, "width", null, true) is { } declared)
		{
			min = declared;
			max = declared;
		}
		else
		{
			(min, max) = Intrinsic(child);
		}

		min = ClampWidth(child, min, null);
		max = ClampWidth(child, max, null);
		return (min + margin.Horizontal, max + margin.Horizontal);
	}

	private void LayoutAbsoluteChildren(Element element, Edges padding, Edges border)
	{
		var box = element.Box;
		var blockWidth = box.Width - border.Horizontal;
		var blockHeight = box.Height - border.Vertical;
		foreach (var child in element.Children)
		{
			if (!Displayed(child))
			{
				child.Box.Displayed = false;
				continue;
			}

			if (!child.Computed.IsAbsolute)
			{
				continue;
			}

			LayoutAbsolute(child, blockWidth, blockHeight, border, padding);
		}
	}

	private void LayoutAbsolute(
		Element child,
		LayoutUnit blockWidth,
		LayoutUnit blockHeight,
		Edges border,
		Edges padding)
	{
		var style = child.Computed;

		LayoutUnit? Inset(string name, LayoutUnit percentBase)
		{
			var length = style.Length(name);
			return length.IsValue
				? LayoutUnit.FromDouble(length.Resolve(percentBase.Value, style.FontSize, style.ZeroWidth))
				: null;
		}

		var left = Inset("left", blockWidth);
		var right = Inset("right", blockWidth);
		var top = Inset("top", blockHeight);
		var bottom = Inset("bottom", blockHeight);
		var margin = Margin(style, blockWidth);
		LayoutUnit? width = SizeProperty(child, "width", blockWidth, true) is { } declaredWidth
			? ClampWidth(child, declaredWidth, blockWidth)
			: null;
		if (width is null && left is not null && right is not null)
		{
			width = ClampWidth(child,
				(blockWidth - left.Value - right.Value - margin.Horizontal).ClampNegativeToZero(),
				blockWidth);
		}

		LayoutUnit? height = SizeProperty(child, "height", blockHeight, false) is { } declaredHeight
			? ClampHeight(child, declaredHeight, blockHeight)
			: null;
		if (height is null && top is not null && bottom is not null)
		{
			height = ClampHeight(child,
				(blockHeight - top.Value - bottom.Value - margin.Vertical).ClampNegativeToZero(),
				blockHeight);
		}

		var available = (blockWidth - (left ?? LayoutUnit.Zero) - (right ?? LayoutUnit.Zero)).ClampNegativeToZero();
		Layout(child, new Constraint(available, width, height, blockWidth, blockHeight, true));

		var usedWidth = child.Box.Width;
		var usedHeight = child.Box.Height;
		LayoutUnit x;
		if (left is not null && right is not null && AutoMargin(style, "left") && AutoMargin(style, "right"))
		{
			x = left.Value + (blockWidth - left.Value - right.Value - usedWidth) / 2;
		}
		else if (left is not null)
		{
			x = left.Value + margin.Left;
		}
		else if (right is not null)
		{
			x = blockWidth - right.Value - usedWidth - margin.Right;
		}
		else
		{
			x = padding.Left + margin.Left;
		}

		LayoutUnit y;
		if (top is not null && bottom is not null && AutoMargin(style, "top") && AutoMargin(style, "bottom"))
		{
			y = top.Value + (blockHeight - top.Value - bottom.Value - usedHeight) / 2;
		}
		else if (top is not null)
		{
			y = top.Value + margin.Top;
		}
		else if (bottom is not null)
		{
			y = blockHeight - bottom.Value - usedHeight - margin.Bottom;
		}
		else
		{
			y = padding.Top + margin.Top;
		}

		child.Box.X = border.Left + x;
		child.Box.Y = border.Top + y;
	}
}
