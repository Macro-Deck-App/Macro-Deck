using System.Text.Json;
using MacroDeckHost.Rendering.Dom;
using MacroDeckHost.Rendering.Tree;

namespace MacroDeckHost.Rendering.Nodes;

internal static class TextComponents
{
	private const double MaxOutlineOfBasis = 0.1;

	public static readonly ComponentDefinition Text = new(Components.Text, "span", PaintText, 1, TextIntrinsic);

	public static readonly ComponentDefinition DynamicText = new(Components.DynamicText,
		"span",
		PaintDynamicText,
		2,
		TextIntrinsic,
		false,
		null,
		TimeFormats.TickPeriodMs);

	public static readonly ComponentDefinition ProgressText = new(Components.ProgressText,
		"span",
		(node, scope) => PaintCommon(node, scope, "widget-progress-text", ProgressRun(node, scope), null),
		1,
		null,
		false,
		null,
		node => Progress.Of(node, "value") is { Rate: not 0 } ? 1000 : null);

	public static readonly ComponentDefinition TextField = new(Components.TextField,
		"input",
		PaintTextField,
		1,
		(node, metrics) => metrics.Horizontal
			? 0
			: (UiLength.Resolve(UiLength.Of(node, "size"), metrics.Scope) ?? 0) * (1.2 + 2 * 0.4) + 2);

	public static double TextIntrinsic(UiNode node, IntrinsicMetrics metrics)
	{
		var size = UiLength.Resolve(UiLength.Of(node, "size"), metrics.Scope);
		return metrics.Horizontal ? 0 : (size ?? 0) * Math.Max(1, node.Number("maxLines") ?? 1);
	}

	private static void PaintText(UiNode node, NodeScope scope)
	{
		PaintCommon(node, scope, null, scope.Host.Text(node, "text") ?? "", Spans(node));
	}

	private static string ProgressRun(UiNode node, NodeScope scope)
	{
		return Progress.Of(node, "value") is { } reference
			? Progress.FormatRun(reference, node.String("format"), scope.Host.NowMs)
			: "";
	}

	private static void PaintCommon(
		UiNode node,
		NodeScope scope,
		string? extraClass,
		string text,
		List<TextSpan>? spans)
	{
		var element = scope.Element;
		var maxLines = Layouts.MaxLines(node);
		var wraps = Layouts.Wraps(node);
		var digits = Layouts.Digits(node);
		var outlineColor = UiLength.HexColor(node, "strokeColor");
		var outlineLength = UiLength.Of(node, "strokeWidth");
		var outlineWidth = UiLength.Resolve(outlineLength, scope.Scope.WithoutCross());
		double? outline = outlineColor is not null && outlineWidth is > 0 && double.IsFinite(outlineWidth.Value)
			? Math.Min(outlineWidth.Value, UiLength.Reference(outlineLength, scope.Scope) * MaxOutlineOfBasis)
			: null;
		var shadowOff = node.Boolean("shadow") == false;

		element.SetClassName(extraClass is null ? "widget-text" : $"widget-text {extraClass}");
		element.SetClass("widget-text-clamp", maxLines > 1);
		element.SetClass("widget-text-tabular", digits is not null);
		element.SetClass("widget-text-no-shadow", shadowOff);
		element.SetClass("widget-text-outlined", outline is not null);
		element.SetStyle("width", NodeScope.Px(scope.Box.Width is { } width ? width + 2 * (outline ?? 0) : null));
		if (outline is { } stroke)
		{
			var px = NodeScope.Px(stroke);
			element.SetStyle("text-shadow", "none");
			element.SetAttribute("data-outline-color", outlineColor);
			element.SetAttribute("data-outline-width", NodeScope.Num(stroke));
			element.SetAttribute("data-outline-shadow", !shadowOff && scope.InsideButton ? "true" : null);
			element.SetStyle("padding-left", px);
			element.SetStyle("padding-right", px);
			element.SetStyle("margin-left", NodeScope.Px(-stroke));
			element.SetStyle("margin-right", NodeScope.Px(-stroke));
			element.SetStyle("padding-top", $"calc(0.2em + {px})");
			element.SetStyle("padding-bottom", $"calc(0.2em + {px})");
			element.SetStyle("margin-top", $"calc(-0.2em - {px})");
			element.SetStyle("margin-bottom", $"calc(-0.2em - {px})");
			element.SetStyle("max-height", $"calc(100% + 0.4em + {NodeScope.Px(2 * stroke)})");
			element.SetStyle("max-width", $"calc(100% + {NodeScope.Px(2 * stroke)})");
		}

		element.SetStyle("font-weight", NodeScope.Num(Layouts.FontWeight(node.String("weight"))));
		element.SetStyle("color", Layouts.FillColor(node));
		element.SetStyle("text-align", Layouts.TextAlign(node));
		element.SetStyle("white-space", wraps ? "pre-wrap" : maxLines <= 1 ? "nowrap" : null);
		element.SetStyle("overflow-wrap", wraps ? "anywhere" : null);
		element.SetStyle("text-overflow", !wraps && maxLines <= 1 ? "ellipsis" : null);
		element.SetStyle("-webkit-line-clamp",
			maxLines > 1 ? maxLines.ToString(System.Globalization.CultureInfo.InvariantCulture) : null);
		element.SetStyle("min-width", digits is { } count ? $"{NodeScope.Num(count)}ch" : null);

		if (spans is not null)
		{
			PaintSpans(element, spans, scope);
		}
		else if (text.Length > 0)
		{
			element.Append(new Element(Element.TextTag, wraps && text.EndsWith('\n') ? text + "\n" : text));
		}

		var size = scope.Resolve("size");
		var minSize = scope.Resolve("minSize");
		element.SetStyle("font-size", NodeScope.Px(size));
		scope.KeepFit(element, size, minSize, maxLines);
	}

	private sealed record TextSpan(
		string? Text,
		string? Color,
		string? Weight,
		bool Italic,
		bool Underline,
		bool Strike,
		bool Mono,
		string? Background,
		JsonElement? Image,
		string? Alt);

	private static List<TextSpan>? Spans(UiNode node)
	{
		if (node.Raw("spans") is not { ValueKind: JsonValueKind.Array } raw)
		{
			return null;
		}

		var spans = new List<TextSpan>();
		foreach (var entry in raw.EnumerateArray())
		{
			if (entry.ValueKind != JsonValueKind.Object)
			{
				continue;
			}

			bool Flag(string name)
			{
				return entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
			}

			string? Str(string name)
			{
				return entry.TryGetProperty(name, out var value)
					&& value.ValueKind == JsonValueKind.String
					&& value.GetString() is { Length: > 0 } text
						? text
						: null;
			}

			JsonElement? Member(string name)
			{
				return entry.TryGetProperty(name, out var value) ? value : null;
			}

			if (entry.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
			{
				spans.Add(new TextSpan(text.GetString(),
					UiLength.HexColor(Member("color")),
					Str("weight"),
					Flag("italic"),
					Flag("underline"),
					Flag("strikethrough"),
					Flag("monospace"),
					UiLength.HexColor(Member("background")),
					null,
					null));
				continue;
			}

			if (Member("image") is { ValueKind: JsonValueKind.Object } image
				&& image.TryGetProperty("resourceId", out var id)
				&& id.ValueKind == JsonValueKind.String
				&& id.GetString() is { Length: > 0 })
			{
				spans.Add(new TextSpan(null, null, null, false, false, false, false, null, image, Str("alt")));
			}
		}

		return spans;
	}

	private static void PaintSpans(Element element, List<TextSpan> spans, NodeScope scope)
	{
		foreach (var span in spans)
		{
			if (span.Text is { } text)
			{
				var run = new Element("span");
				run.Append(new Element(Element.TextTag, text));
				if (span.Color is { } color)
				{
					run.SetStyle("color", color);
				}

				if (span.Weight is { } weight)
				{
					run.SetStyle("font-weight", NodeScope.Num(Layouts.FontWeight(weight)));
				}

				if (span.Italic)
				{
					run.SetStyle("font-style", "italic");
				}

				var lines = string.Join(' ',
					new[] { span.Underline ? "underline" : "", span.Strike ? "line-through" : "" }
						.Where(part => part.Length > 0));
				run.SetStyle("text-decoration", lines.Length > 0 ? lines : null);
				if (span.Mono)
				{
					run.SetStyle("font-family", "var(--font-mono, ui-monospace, Menlo, Consolas, monospace)");
				}

				if (span.Background is { } background)
				{
					run.SetStyle("background-color", background);
					run.SetStyle("padding", "0 0.2em");
					run.SetStyle("border-radius", "0.25em");
				}

				element.Append(run);
				continue;
			}

			if (scope.Host.Resource(span.Image, null) is { } resource)
			{
				var image = new Element("img") { Paint = resource };
				image.SetClassName("widget-text-span-image");
				image.SetAttribute("data-resource-id", span.Image?.GetProperty("resourceId").GetString());
				image.SetAttribute("alt", span.Alt ?? "");
				element.Append(image);
			}
			else if (span.Alt is { } alt)
			{
				var caption = new Element("span");
				caption.Append(new Element(Element.TextTag, alt));
				element.Append(caption);
			}
		}
	}

	private static void PaintDynamicText(UiNode node, NodeScope scope)
	{
		var element = scope.Element;
		element.SetClassName("widget-text widget-dynamic-text");
		element.SetStyle("width", NodeScope.Px(scope.Box.Width));
		element.SetStyle("font-weight", NodeScope.Num(Layouts.FontWeight(node.String("weight"))));
		element.SetStyle("color", Layouts.FillColor(node));
		element.SetStyle("text-align", Layouts.TextAlign(node));
		element.SetStyle("white-space", "nowrap");
		element.SetStyle("text-overflow", "ellipsis");
		var run = TimeFormats.Run(node, scope.Host);
		element.Append(new Element(Element.TextTag, run.Before));
		var seconds = scope.Part("seconds", "span");
		seconds.SetClassName("widget-dynamic-text-seconds");
		seconds.Append(new Element(Element.TextTag, run.Seconds));
		seconds.SetStyle("color", "var(--color-text-muted)");
		seconds.SetStyle("font-size", "0.55em");
		element.Append(new Element(Element.TextTag, run.After));
		var size = scope.Resolve("size");
		element.SetStyle("font-size", NodeScope.Px(size));
		scope.KeepFit(element, size, scope.Resolve("minSize"), 1);
	}

	private static void PaintTextField(UiNode node, NodeScope scope)
	{
		var element = scope.Element;
		element.SetClassName("widget-text-field");
		element.SetStyle("width", NodeScope.Px(scope.Box.Width));
		element.SetStyle("font-size", NodeScope.Px(scope.Resolve("size")));
		var value = node.String("text") ?? "";
		var placeholder = scope.Host.Text(node, "placeholder") ?? "";
		element.Append(new Element(Element.TextTag, value.Length > 0 ? value : placeholder));
		element.SetAttribute("data-placeholder", value.Length > 0 ? null : "true");
	}
}
