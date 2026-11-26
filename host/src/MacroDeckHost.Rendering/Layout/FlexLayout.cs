using MacroDeckHost.Rendering.Dom;

namespace MacroDeckHost.Rendering.Layout;

// Single-line flex layout, following Chrome's FlexLayoutAlgorithm and FlexLine::ResolveFlexibleLengths:
// the flex math runs on content-box sizes in layout units and each share is rounded to the nearest unit.
internal sealed partial class LayoutEngine
{
	private sealed class FlexItem
	{
		public required Element Element { get; init; }

		public required Edges Margin { get; init; }

		public LayoutUnit MainEdges { get; set; }

		public LayoutUnit BaseContent { get; set; }

		public LayoutUnit HypotheticalContent { get; set; }

		public LayoutUnit? MinContent { get; set; }

		public LayoutUnit? MaxContent { get; set; }

		public LayoutUnit FlexedContent { get; set; }

		public bool Frozen { get; set; }

		public double Grow { get; set; }

		public double Shrink { get; set; }

		public LayoutUnit Main => FlexedContent + MainEdges;

		public LayoutUnit Clamp(LayoutUnit size)
		{
			if (MaxContent is { } max)
			{
				size = LayoutUnit.Min(size, max);
			}

			if (MinContent is { } min)
			{
				size = LayoutUnit.MaxOf(size, min);
			}

			return size.ClampNegativeToZero();
		}
	}

	private (LayoutUnit Min, LayoutUnit Max) FlexIntrinsic(Element element)
	{
		var style = element.Computed;
		var column = style.IsColumn;
		var gap = Length(style, column ? "row-gap" : "column-gap", null);
		var min = LayoutUnit.Zero;
		var max = LayoutUnit.Zero;
		var count = 0;
		foreach (var child in element.Children)
		{
			if (!Displayed(child) || child.Computed.IsAbsolute)
			{
				continue;
			}

			var contribution = Contribution(child);
			if (column)
			{
				min = LayoutUnit.MaxOf(min, contribution.Min);
				max = LayoutUnit.MaxOf(max, contribution.Max);
			}
			else
			{
				min += contribution.Min;
				max += contribution.Max;
			}

			count++;
		}

		if (!column && count > 1)
		{
			min += gap * (count - 1);
			max += gap * (count - 1);
		}

		return (min, max);
	}

	private ContentSize LayoutFlex(
		Element element,
		LayoutUnit contentWidth,
		LayoutUnit? contentHeight,
		LayoutUnit offsetX,
		LayoutUnit offsetY)
	{
		var style = element.Computed;
		var column = style.IsColumn;
		var mainSize = column ? contentHeight : contentWidth;
		var crossSize = column ? contentWidth : contentHeight;
		var gap = Length(style, column ? "row-gap" : "column-gap", contentWidth);
		var items = new List<FlexItem>();
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

			items.Add(BuildFlexItem(child, column, contentWidth, mainSize));
		}

		var gaps = items.Count > 1 ? gap * (items.Count - 1) : LayoutUnit.Zero;
		if (mainSize is { } definiteMain)
		{
			ResolveFlexibleLengths(items, definiteMain - gaps);
		}
		else
		{
			foreach (var item in items)
			{
				item.FlexedContent = item.HypotheticalContent;
			}
		}

		var align = style.Get("align-items", "normal");
		var crossSizes = new List<LayoutUnit>();
		foreach (var item in items)
		{
			LayoutItem(item, column, contentWidth, contentHeight, crossSize, null);
			crossSizes.Add(column ? item.Element.Box.Width : item.Element.Box.Height);
		}

		var lineCross = crossSize ?? LayoutUnit.Zero;
		var maxAscent = LayoutUnit.Zero;
		var maxDescent = LayoutUnit.Zero;
		var baselines = new LayoutUnit[items.Count];
		for (var index = 0; index < items.Count; index++)
		{
			var item = items[index];
			var itemAlign = AlignOf(item.Element, align);
			var crossMargins = column ? item.Margin.Horizontal : item.Margin.Vertical;
			if (!column && itemAlign == "baseline")
			{
				var box = item.Element.Box;
				var baseline = item.Margin.Top + (box.HasBaseline ? box.Baseline : box.Height);
				baselines[index] = baseline;
				maxAscent = LayoutUnit.MaxOf(maxAscent, baseline);
				maxDescent = LayoutUnit.MaxOf(maxDescent, box.Height + crossMargins - baseline);
			}

			if (crossSize is null)
			{
				lineCross = LayoutUnit.MaxOf(lineCross, crossSizes[index] + crossMargins);
			}
		}

		if (crossSize is null)
		{
			lineCross = LayoutUnit.MaxOf(lineCross, maxAscent + maxDescent);
		}

		for (var index = 0; index < items.Count; index++)
		{
			var item = items[index];
			if (Stretches(item.Element, align, column))
			{
				var crossMargins = column ? item.Margin.Horizontal : item.Margin.Vertical;
				var stretched = (lineCross - crossMargins).ClampNegativeToZero();
				stretched = column
					? ClampWidth(item.Element, stretched, contentWidth)
					: ClampHeight(item.Element, stretched, contentHeight);
				if (stretched != crossSizes[index] || crossSize is null)
				{
					LayoutItem(item, column, contentWidth, contentHeight, crossSize, stretched);
				}
			}
		}

		var used = gaps;
		foreach (var item in items)
		{
			used += item.Main + (column ? item.Margin.Vertical : item.Margin.Horizontal);
		}

		var free = (mainSize ?? used) - used;
		var justify = style.Get("justify-content", "normal");
		var position = LayoutUnit.Zero;
		var between = LayoutUnit.Zero;
		switch (justify)
		{
			case "flex-end":
			case "end":
				position = free;
				break;
			case "center":
				position = free / 2;
				break;
			case "space-between":
				if (free > LayoutUnit.Zero && items.Count > 1)
				{
					between = free / (items.Count - 1);
				}

				break;
		}

		var extentMain = LayoutUnit.Zero;
		var extentCross = LayoutUnit.Zero;
		for (var index = 0; index < items.Count; index++)
		{
			var item = items[index];
			var box = item.Element.Box;
			var itemAlign = AlignOf(item.Element, align);
			var itemCross = column ? box.Width : box.Height;
			var crossMargins = column ? item.Margin.Horizontal : item.Margin.Vertical;
			var available = lineCross - itemCross - crossMargins;
			var crossOffset = itemAlign switch
			{
				"center" => available / 2,
				"flex-end" or "end" or "self-end" => available,
				"baseline" when !column => maxAscent - baselines[index],
				_ => LayoutUnit.Zero
			};

			if (column)
			{
				position += item.Margin.Top;
				box.X = offsetX + crossOffset + item.Margin.Left;
				box.Y = offsetY + position;
				position += box.Height + item.Margin.Bottom + gap + between;
			}
			else
			{
				position += item.Margin.Left;
				box.X = offsetX + position;
				box.Y = offsetY + crossOffset + item.Margin.Top;
				position += box.Width + item.Margin.Right + gap + between;
			}

			extentMain = LayoutUnit.MaxOf(extentMain,
				column
					? box.Y + box.Height + item.Margin.Bottom - offsetY
					: box.X + box.Width + item.Margin.Right - offsetX);
			extentCross = LayoutUnit.MaxOf(extentCross, itemCross + crossMargins + crossOffset);
			if (index == 0 || !element.Box.HasBaseline)
			{
				if (box.HasBaseline)
				{
					element.Box.Baseline = box.Y + box.Baseline;
					element.Box.HasBaseline = true;
				}
			}
		}

		var mainExtent = LayoutUnit.MaxOf(used, extentMain);
		return column
			? new ContentSize(LayoutUnit.MaxOf(lineCross, extentCross), mainSize is null ? used : mainExtent)
			: new ContentSize(mainSize is null ? used : mainExtent,
				crossSize is null ? lineCross : LayoutUnit.MaxOf(lineCross, extentCross));
	}

	private static string AlignOf(Element item, string containerAlign)
	{
		var self = item.Computed.Get("align-self", "auto");
		var value = self == "auto" ? containerAlign : self;
		return value is "normal" ? "stretch" : value;
	}

	private static bool Stretches(Element item, string containerAlign, bool column)
	{
		if (AlignOf(item, containerAlign) != "stretch")
		{
			return false;
		}

		var style = item.Computed;
		var crossProperty = style.Length(column ? "width" : "height");
		if (!crossProperty.IsAuto)
		{
			return false;
		}

		if (column
			? AutoMargin(style, "left") || AutoMargin(style, "right")
			: AutoMargin(style, "top") || AutoMargin(style, "bottom"))
		{
			return false;
		}

		return !(column
			? IsReplaced(item) && ReplacedWidth(item) is not null
			: IsReplaced(item) && ReplacedHeight(item) is not null);
	}

	private FlexItem BuildFlexItem(Element child, bool column, LayoutUnit contentWidth, LayoutUnit? mainSize)
	{
		var style = child.Computed;
		var item = new FlexItem
		{
			Element = child,
			Margin = Margin(style, contentWidth),
			Grow = style.FlexGrow,
			Shrink = style.FlexShrink
		};
		var padding = Padding(style, contentWidth);
		var border = Border(style);
		item.MainEdges = column ? padding.Vertical + border.Vertical : padding.Horizontal + border.Horizontal;
		var mainProperty = column ? "height" : "width";

		LayoutUnit baseSize;
		var basis = style.Length("flex-basis");
		if (basis.IsValue && (!basis.HasPercent || mainSize is not null))
		{
			baseSize = LayoutUnit.FromDouble(basis.Resolve(mainSize?.Value ?? 0, style.FontSize, style.ZeroWidth));
			if (!style.BorderBox)
			{
				baseSize += item.MainEdges;
			}
		}
		else if (SizeProperty(child, mainProperty, mainSize, !column) is { } declared)
		{
			baseSize = declared;
		}
		else if (!column)
		{
			baseSize = IsReplaced(child) ? ReplacedWidth(child) ?? item.MainEdges : Intrinsic(child).Max;
		}
		else if (IsReplaced(child) && ReplacedHeight(child) is { } replacedHeight)
		{
			baseSize = replacedHeight;
		}
		else
		{
			var cross = ColumnItemWidth(child,
				item.Margin,
				contentWidth,
				child.Parent!.Computed.Get("align-items", "normal"));
			Layout(child, new Constraint(cross, cross, null, contentWidth, null, false));
			baseSize = child.Box.Height;
		}

		var maxMain = SizeProperty(child, column ? "max-height" : "max-width", mainSize, !column);
		var minMain = SizeProperty(child, column ? "min-height" : "min-width", mainSize, !column);
		item.BaseContent = (baseSize - item.MainEdges).ClampNegativeToZero();
		item.MaxContent = maxMain is { } max ? (max - item.MainEdges).ClampNegativeToZero() : null;
		item.MinContent = minMain is { } min ? (min - item.MainEdges).ClampNegativeToZero() : null;
		item.HypotheticalContent = item.Clamp(item.BaseContent);
		return item;
	}

	private LayoutUnit ColumnItemWidth(Element child, Edges margin, LayoutUnit contentWidth, string containerAlign)
	{
		if (SizeProperty(child, "width", contentWidth, true) is { } declared)
		{
			return ClampWidth(child, declared, contentWidth);
		}

		var available = (contentWidth - margin.Horizontal).ClampNegativeToZero();
		if (Stretches(child, containerAlign, true))
		{
			return ClampWidth(child, available, contentWidth);
		}

		if (IsReplaced(child) && ReplacedWidth(child) is { } replaced)
		{
			return ClampWidth(child, replaced, contentWidth);
		}

		var (min, max) = Intrinsic(child);
		return ClampWidth(child, LayoutUnit.Min(max, LayoutUnit.MaxOf(min, available)), contentWidth);
	}

	private void LayoutItem(
		FlexItem item,
		bool column,
		LayoutUnit contentWidth,
		LayoutUnit? contentHeight,
		LayoutUnit? crossSize,
		LayoutUnit? stretchedCross)
	{
		var child = item.Element;
		var main = item.Main;
		if (column)
		{
			var width = stretchedCross
				?? ColumnItemWidth(child,
					item.Margin,
					contentWidth,
					child.Parent!.Computed.Get("align-items", "normal"));
			Layout(child, new Constraint(width, width, main, contentWidth, contentHeight, false));
		}
		else
		{
			var height = stretchedCross;
			if (height is null
				&& crossSize is not null
				&& Stretches(child, child.Parent!.Computed.Get("align-items", "normal"), false))
			{
				height = ClampHeight(child,
					(crossSize.Value - item.Margin.Vertical).ClampNegativeToZero(),
					contentHeight);
			}

			Layout(child, new Constraint(main, main, height, contentWidth, contentHeight, false));
		}
	}

	private static void ResolveFlexibleLengths(List<FlexItem> items, LayoutUnit containerInner)
	{
		var sumHypothetical = LayoutUnit.Zero;
		var sumBase = LayoutUnit.Zero;
		foreach (var item in items)
		{
			sumHypothetical += item.HypotheticalContent + item.MainEdges + MainMargins(item);
			sumBase += item.BaseContent + item.MainEdges + MainMargins(item);
		}

		var positive = sumHypothetical < containerInner;
		var remaining = containerInner - sumBase;
		double totalGrow = 0;
		double totalShrink = 0;
		double totalWeightedShrink = 0;
		foreach (var item in items)
		{
			item.Frozen = false;
			totalGrow += item.Grow;
			totalShrink += item.Shrink;
			totalWeightedShrink += item.Shrink * item.BaseContent.Value;
		}

		void Freeze(List<FlexItem> violations)
		{
			foreach (var item in violations)
			{
				remaining -= item.FlexedContent - item.BaseContent;
				totalGrow -= item.Grow;
				totalShrink -= item.Shrink;
				totalWeightedShrink = Math.Max(0, totalWeightedShrink - item.Shrink * item.BaseContent.Value);
				item.Frozen = true;
			}
		}

		var inflexible = new List<FlexItem>();
		foreach (var item in items)
		{
			var factor = positive ? item.Grow : item.Shrink;
			if (factor == 0
				|| positive && item.BaseContent > item.HypotheticalContent
				|| !positive && item.BaseContent < item.HypotheticalContent)
			{
				item.FlexedContent = item.HypotheticalContent;
				inflexible.Add(item);
			}
		}

		Freeze(inflexible);
		var initialFree = remaining;
		for (var guard = 0; guard <= items.Count; guard++)
		{
			if (items.All(item => item.Frozen))
			{
				break;
			}

			var sumFactors = positive ? totalGrow : totalShrink;
			if (sumFactors > 0 && sumFactors < 1)
			{
				var fractional = LayoutUnit.FromDouble(initialFree.Value * sumFactors);
				if (Math.Abs(fractional.Raw) < Math.Abs(remaining.Raw))
				{
					remaining = fractional;
				}
			}

			var totalViolation = LayoutUnit.Zero;
			var usedFree = LayoutUnit.Zero;
			var minViolations = new List<FlexItem>();
			var maxViolations = new List<FlexItem>();
			foreach (var item in items)
			{
				if (item.Frozen)
				{
					continue;
				}

				var size = item.BaseContent;
				double extra = 0;
				if (remaining > LayoutUnit.Zero && totalGrow > 0 && positive)
				{
					extra = remaining.Value * item.Grow / totalGrow;
				}
				else if (remaining < LayoutUnit.Zero && totalWeightedShrink > 0 && !positive && item.Shrink != 0)
				{
					extra = remaining.Value * item.Shrink * item.BaseContent.Value / totalWeightedShrink;
				}

				if (double.IsFinite(extra))
				{
					size += LayoutUnit.FromDoubleRound(extra);
				}

				var adjusted = item.Clamp(size);
				item.FlexedContent = adjusted;
				usedFree += adjusted - item.BaseContent;
				var violation = adjusted - size;
				if (violation > LayoutUnit.Zero)
				{
					minViolations.Add(item);
				}
				else if (violation < LayoutUnit.Zero)
				{
					maxViolations.Add(item);
				}

				totalViolation += violation;
			}

			if (totalViolation != LayoutUnit.Zero)
			{
				Freeze(totalViolation < LayoutUnit.Zero ? maxViolations : minViolations);
			}
			else
			{
				break;
			}
		}
	}

	private static LayoutUnit MainMargins(FlexItem item)
	{
		var column = item.Element.Parent!.Computed.IsColumn;
		return column ? item.Margin.Vertical : item.Margin.Horizontal;
	}
}
