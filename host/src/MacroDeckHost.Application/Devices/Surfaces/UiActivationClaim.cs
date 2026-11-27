using System.Text.Json;
using MacroDeck.Ui.Components;
using MacroDeckHost.Domain.Common;

namespace MacroDeckHost.Application.Devices.Surfaces;

// The host's reading of activationClaim and activationFor in ui/runtime/src/ui-framework/node-gestures.ts.
// The two must agree, except that only a hardware press steps a ui.slider: client keys leave it alone.
public readonly record struct UiActivationClaim(JsonElement? Claimant, bool Absorbed)
{
	private const double MinimumSliderPressStep = 0.1;
	private const double SliderTolerance = 1e-9;

	public static UiActivationClaim Of(JsonElement tree, double? widthCells = null, double? heightCells = null)
	{
		var absorbed = false;
		var claimant = tree.ValueKind == JsonValueKind.Object && tree.TryGetProperty("root", out var root)
			? Visit(root, widthCells, heightCells, ref absorbed)
			: null;

		return new UiActivationClaim(claimant, claimant is null && absorbed);
	}

	public static (string Name, object? Payload)? EventFor(JsonElement node, string triggerType)
	{
		if (string.Equals(triggerType, WidgetTriggerTypes.ShortPress, StringComparison.Ordinal)
			&& IsType(node, UiComponents.Slider)
			&& Declares(node, UiComponentEvents.Change))
		{
			return (UiComponentEvents.Change, NextSliderLevel(node));
		}

		if (string.Equals(triggerType, WidgetTriggerTypes.ShortPress, StringComparison.Ordinal)
			&& ChangeFor(node) is { } change)
		{
			return (UiComponentEvents.Change, change);
		}

		var name = triggerType switch
		{
			WidgetTriggerTypes.ShortPress => UiComponentEvents.Press,
			WidgetTriggerTypes.LongPress => UiComponentEvents.LongPress,
			WidgetTriggerTypes.TouchStart => UiComponentEvents.PressStart,
			WidgetTriggerTypes.TouchEnd => UiComponentEvents.PressEnd,
			_ => null
		};

		return name is not null && Declares(node, name) ? (name, null) : null;
	}

	private static JsonElement? Visit(JsonElement node, double? widthCells, double? heightCells, ref bool absorbed)
	{
		if (node.ValueKind != JsonValueKind.Object)
		{
			return null;
		}

		if (IsDisabledRegion(node))
		{
			absorbed = true;
			return null;
		}

		if (Declares(node, UiComponentEvents.Press)
			|| Declares(node, UiComponentEvents.LongPress)
			|| Declares(node, UiComponentEvents.PressStart)
			|| Declares(node, UiComponentEvents.PressEnd)
			|| Declares(node, UiComponentEvents.Adjust)
			|| Declares(node, UiComponentEvents.Change))
		{
			return node;
		}

		if (node.TryGetProperty("type", out var type) && type.ValueEquals(UiComponents.Segmented))
		{
			return null;
		}

		if (!node.TryGetProperty("children", out var children) || children.ValueKind != JsonValueKind.Array)
		{
			return null;
		}

		if (IsType(node, UiComponents.Responsive))
		{
			var variants = node.TryGetProperty("properties", out var responsiveProperties)
				&& responsiveProperties.ValueKind == JsonValueKind.Object
				&& responsiveProperties.TryGetProperty(UiComponentProperties.Variants, out var declared)
					? declared
					: default;
			var index = UiResponsiveSelection.SelectChild(variants, children.GetArrayLength(), widthCells, heightCells);

			return index < 0 ? null : Visit(children[index], widthCells, heightCells, ref absorbed);
		}

		var passesBox = PassesWholeBox(node);
		foreach (var child in children.EnumerateArray())
		{
			if (Visit(child, passesBox ? widthCells : null, passesBox ? heightCells : null, ref absorbed) is { } found)
			{
				return found;
			}
		}

		return null;
	}

	private static bool IsType(JsonElement node, string type)
	{
		return node.TryGetProperty("type", out var value) && value.ValueEquals(type);
	}

	private static bool PassesWholeBox(JsonElement node)
	{
		if (IsType(node, UiComponents.Layer) || IsType(node, UiComponents.Transform))
		{
			return true;
		}

		if (!IsType(node, UiComponents.Modifier))
		{
			return false;
		}

		return !node.TryGetProperty("properties", out var properties)
			|| properties.ValueKind != JsonValueKind.Object
			|| !properties.TryGetProperty(UiComponentProperties.Padding, out _)
			&& !properties.TryGetProperty(UiComponentProperties.Frame, out _);
	}

	private static object? ChangeFor(JsonElement node)
	{
		if (!Declares(node, UiComponentEvents.Change))
		{
			return null;
		}

		var type = node.TryGetProperty("type", out var typeValue) ? typeValue.GetString() : null;
		if (string.Equals(type, UiComponents.Toggle, StringComparison.Ordinal))
		{
			return Property(node, UiComponentProperties.On) is not { ValueKind: JsonValueKind.True };
		}

		if (!string.Equals(type, UiComponents.Segmented, StringComparison.Ordinal))
		{
			return null;
		}

		var count = node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array
			? children.GetArrayLength()
			: 0;
		if (count == 0)
		{
			return null;
		}

		var current = Property(node, UiComponentProperties.Selected) is { ValueKind: JsonValueKind.Number } selected
			&& selected.GetDouble() is var value and >= 0
			&& value < count
				? (int)Math.Floor(value)
				: -1;

		return (current + 1) % count;
	}

	private static double NextSliderLevel(JsonElement node)
	{
		var level = Property(node, UiComponentProperties.Level) is { ValueKind: JsonValueKind.Number } declared
			? Math.Clamp(declared.GetDouble(), 0, 1)
			: 0;
		if (level >= 1 - SliderTolerance)
		{
			return 0;
		}

		double? grid = Property(node, UiComponentProperties.Step) is { ValueKind: JsonValueKind.Number } step
			&& step.GetDouble() is var value and > 0 and <= 1
				? value
				: null;
		var pressStep = grid is { } size
			? Math.Ceiling(MinimumSliderPressStep / size - SliderTolerance) * size
			: MinimumSliderPressStep;

		var next = Math.Min(level + pressStep, 1);
		if (grid is { } snap)
		{
			next = Math.Round(next / snap, MidpointRounding.AwayFromZero) * snap;
		}

		next = Math.Clamp(next, 0, 1);

		return next <= level + SliderTolerance ? 0 : next;
	}

	private static bool IsDisabledRegion(JsonElement node)
	{
		return Property(node, UiComponentProperties.Modifiers) is { ValueKind: JsonValueKind.Object } modifiers
			&& modifiers.TryGetProperty(UiComponentModifiers.Disabled, out var disabled)
			&& disabled.ValueKind == JsonValueKind.True;
	}

	private static bool Declares(JsonElement node, string eventName)
	{
		if (Property(node, UiComponentProperties.Events) is not { ValueKind: JsonValueKind.Array } events)
		{
			return false;
		}

		foreach (var declared in events.EnumerateArray())
		{
			if (declared.ValueKind == JsonValueKind.String && declared.ValueEquals(eventName))
			{
				return true;
			}
		}

		return false;
	}

	private static JsonElement? Property(JsonElement node, string key)
	{
		return node.TryGetProperty("properties", out var properties)
			&& properties.ValueKind == JsonValueKind.Object
			&& properties.TryGetProperty(key, out var value)
				? value
				: null;
	}
}
