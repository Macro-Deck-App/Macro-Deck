using System.Text.Json;
using MacroDeckHost.Rendering.Tree;

namespace MacroDeckHost.Rendering.Nodes;

internal sealed record ModifierBorder(string Width, string Line, string Color);

internal sealed record ModifierPlan(
	Dictionary<string, string> Styles,
	Dictionary<string, string> Attributes,
	ModifierBorder? Border);

internal static class Modifiers
{
	public const double DimOpacity = 0.4;

	private static readonly string[] _borderLines = ["solid", "dashed", "dotted"];

	private static readonly string[] _gestureEvents =
		["drag", "drag-end", "swipe", "pinch", "pinch-end", "pointer-down", "pointer-move", "pointer-up", "tap"];

	private static readonly string[] _nativeRoleTags = ["input", "textarea", "select", "img", "svg"];

	private static readonly string[] _overlayBorderTypes =
	[
		Components.Stack, Components.Button, Components.Layer, Components.Transform, Components.Modifier,
		Components.Grid, Components.Toggle, Components.Segmented, Components.Responsive, Components.FirstFit
	];

	public static bool IsDisabledRegion(UiNode node)
	{
		return node.Record("modifiers") is { } modifiers
			&& modifiers.TryGetProperty("disabled", out var disabled)
			&& disabled.ValueKind == JsonValueKind.True;
	}

	public static bool Emits(UiNode node, string name)
	{
		return node.Events().Contains(name);
	}

	public static bool ClaimsGesture(UiNode node)
	{
		return Emits(node, "press")
			|| Emits(node, "long-press")
			|| Emits(node, "press-start")
			|| Emits(node, "press-end");
	}

	public static bool ClaimsValue(UiNode node)
	{
		return Emits(node, "adjust") || Emits(node, "change");
	}

	public static bool DeclaresGesture(UiNode node)
	{
		return _gestureEvents.Any(name => Emits(node, name));
	}

	public static string Trimmed(double value)
	{
		return RenderHost.FormatNumber(Math.Round(value, 4, MidpointRounding.AwayFromZero));
	}

	public static string? GradientCss(JsonElement? raw, Func<JsonElement, string?> stopColor)
	{
		if (raw is not { ValueKind: JsonValueKind.Object } value)
		{
			return null;
		}

		string? Stops(JsonElement shape)
		{
			if (!shape.TryGetProperty("stops", out var stops)
				|| stops.ValueKind != JsonValueKind.Array
				|| stops.GetArrayLength() < 2)
			{
				return null;
			}

			var parts = new List<string>();
			foreach (var stop in stops.EnumerateArray())
			{
				if (stop.ValueKind != JsonValueKind.Object
					|| !stop.TryGetProperty("offset", out var offset)
					|| offset.ValueKind != JsonValueKind.Number
					|| stopColor(stop) is not { } color)
				{
					return null;
				}

				parts.Add($"{color} {Trimmed(offset.GetDouble() * 100)}%");
			}

			return string.Join(", ", parts);
		}

		if (value.TryGetProperty("linear", out var linear) && linear.ValueKind == JsonValueKind.Object)
		{
			var stops = Stops(linear);
			return linear.TryGetProperty("angle", out var angle)
				&& angle.ValueKind == JsonValueKind.Number
				&& stops is not null
					? $"linear-gradient({Trimmed(angle.GetDouble())}deg, {stops})"
					: null;
		}

		if (value.TryGetProperty("radial", out var radial) && radial.ValueKind == JsonValueKind.Object)
		{
			var stops = Stops(radial);
			if (!radial.TryGetProperty("centerX", out var x)
				|| x.ValueKind != JsonValueKind.Number
				|| !radial.TryGetProperty("centerY", out var y)
				|| y.ValueKind != JsonValueKind.Number
				|| stops is null)
			{
				return null;
			}

			return $"radial-gradient(at {Trimmed(x.GetDouble() * 100)}% {Trimmed(y.GetDouble() * 100)}%, {stops})";
		}

		return null;
	}

	private static string? BackgroundOf(JsonElement modifiers)
	{
		var raw = modifiers.TryGetProperty("background", out var background) ? background : (JsonElement?)null;
		return UiLength.HexColor(raw)
			?? GradientCss(raw, stop => UiLength.HexColor(stop.TryGetProperty("color", out var color) ? color : null));
	}

	private static double? RadiusOf(JsonElement modifiers, NodeScope scope)
	{
		if (scope.IsTreeRoot)
		{
			return null;
		}

		return UiLength.Resolve(UiLength.From(modifiers.TryGetProperty("radius", out var radius) ? radius : null),
			scope.Scope);
	}

	public static bool Owns(UiNode node, NodeScope scope, string property)
	{
		if (node.Record("modifiers") is not { } modifiers)
		{
			return false;
		}

		return property == "background" ? BackgroundOf(modifiers) is not null : RadiusOf(modifiers, scope) is not null;
	}

	public static ModifierPlan Plan(UiNode node, NodeScope scope, ModifierInputs? inputs)
	{
		var styles = new Dictionary<string, string>(StringComparer.Ordinal);
		var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
		var modifiers = node.Record("modifiers");
		var dimmed = modifiers is { } record
			&& record.TryGetProperty("disabled", out var disabled)
			&& disabled.ValueKind == JsonValueKind.True
			&& !scope.ParentDisabled;
		if (inputs?.Opacity is not null || dimmed)
		{
			styles["opacity"] = Trimmed((inputs?.Opacity ?? 1) * (dimmed ? DimOpacity : 1));
		}

		var radius = modifiers is { } withRadius ? RadiusOf(withRadius, scope) : null;
		if (radius is { } resolved)
		{
			styles["border-radius"] = $"{Trimmed(resolved)}px";
		}
		else if (inputs?.Radius is { } inputRadius && !scope.IsTreeRoot)
		{
			styles["border-radius"] = inputRadius;
		}

		if (DeclaresGesture(node))
		{
			styles["touch-action"] = "none";
		}

		ModifierBorder? border = null;
		if (modifiers is { } values)
		{
			if (BackgroundOf(values) is { } background)
			{
				styles["background"] = background;
			}

			var width = UiLength.Resolve(
				UiLength.From(values.TryGetProperty("borderWidth", out var borderWidth) ? borderWidth : null),
				scope.Scope);
			if (width is > 0)
			{
				var requested = values.TryGetProperty("borderLine", out var line)
					&& line.ValueKind == JsonValueKind.String
						? line.GetString()
						: null;
				var lineStyle = requested is not null && _borderLines.Contains(requested) ? requested : "solid";
				var color = UiLength.HexColor(values.TryGetProperty("borderColor", out var borderColor)
						? borderColor
						: null)
					?? "currentColor";
				if (_overlayBorderTypes.Contains(node.Type))
				{
					border = new ModifierBorder($"{Trimmed(width.Value)}px", lineStyle, color);
				}
				else
				{
					styles["outline"] = $"{Trimmed(width.Value)}px {lineStyle} {color}";
					styles["outline-offset"] = $"{Trimmed(-width.Value)}px";
				}
			}

			var label = scope.Host.Text(values.TryGetProperty("accessibilityLabel", out var rawLabel)
				? rawLabel
				: null);
			var hint = scope.Host.Text(values.TryGetProperty("accessibilityHint", out var rawHint) ? rawHint : null);
			if (label is not null)
			{
				attributes["aria-label"] = label;
			}

			if (hint is not null)
			{
				attributes["aria-description"] = hint;
			}

			if ((label is not null || hint is not null) && !_nativeRoleTags.Contains(scope.Element.Tag))
			{
				attributes["role"] = ClaimsGesture(node) ? "button" : "group";
			}
		}

		if (scope.IsDisabled)
		{
			attributes["aria-disabled"] = "true";
		}

		return new ModifierPlan(styles, attributes, border);
	}

	public static void Apply(ModifierPlan plan, NodeScope scope)
	{
		foreach (var (name, value) in plan.Styles)
		{
			scope.Element.SetStyle(name, value);
		}

		foreach (var (name, value) in plan.Attributes)
		{
			scope.Element.SetAttribute(name, value);
		}

		if (plan.Border is { } border)
		{
			var overlay = scope.Part("modifier-border", "div");
			overlay.SetClassName("widget-modifier-border");
			overlay.SetStyle("border-width", border.Width);
			overlay.SetStyle("border-style", border.Line);
			overlay.SetStyle("border-color", border.Color);
		}
	}
}
