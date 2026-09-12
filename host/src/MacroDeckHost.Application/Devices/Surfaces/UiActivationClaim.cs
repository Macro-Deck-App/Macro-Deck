using System.Text.Json;
using MacroDeck.Ui.Components;
using MacroDeckHost.Domain.Common;

namespace MacroDeckHost.Application.Devices.Surfaces;

// The host's reading of activationClaim and activationFor in ui/runtime/src/ui-framework/node-gestures.ts.
// A tile must answer a hardware press exactly as it answers a pointer or a key, so the two must agree.
public readonly record struct UiActivationClaim(JsonElement? Claimant, bool Absorbed)
{
	public static UiActivationClaim Of(JsonElement tree)
	{
		var absorbed = false;
		var claimant = tree.ValueKind == JsonValueKind.Object && tree.TryGetProperty("root", out var root)
			? Visit(root, ref absorbed)
			: null;

		return new UiActivationClaim(claimant, claimant is null && absorbed);
	}

	public static (string Name, object? Payload)? EventFor(JsonElement node, string triggerType)
	{
		if (string.Equals(triggerType, WidgetTriggerTypes.ShortPress, StringComparison.Ordinal) &&
			ChangeFor(node) is { } change)
		{
			return (UiComponentEvents.Change, change);
		}

		string? name = triggerType switch
		{
			WidgetTriggerTypes.ShortPress => UiComponentEvents.Press,
			WidgetTriggerTypes.LongPress => UiComponentEvents.LongPress,
			WidgetTriggerTypes.TouchStart => UiComponentEvents.PressStart,
			WidgetTriggerTypes.TouchEnd => UiComponentEvents.PressEnd,
			_ => null
		};

		return name is not null && Declares(node, name) ? (name, null) : null;
	}

	private static JsonElement? Visit(JsonElement node, ref bool absorbed)
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

		if (Declares(node, UiComponentEvents.Press) ||
			Declares(node, UiComponentEvents.LongPress) ||
			Declares(node, UiComponentEvents.PressStart) ||
			Declares(node, UiComponentEvents.PressEnd) ||
			Declares(node, UiComponentEvents.Adjust) ||
			Declares(node, UiComponentEvents.Change))
		{
			return node;
		}

		if (node.TryGetProperty("type", out var type) && type.ValueEquals(UiComponents.Segmented))
		{
			return null;
		}

		if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
		{
			foreach (var child in children.EnumerateArray())
			{
				if (Visit(child, ref absorbed) is { } found)
				{
					return found;
				}
			}
		}

		return null;
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

		var current = Property(node, UiComponentProperties.Selected) is { ValueKind: JsonValueKind.Number } selected &&
			selected.GetDouble() is var value and >= 0 &&
			value < count
				? (int)Math.Floor(value)
				: -1;

		return (current + 1) % count;
	}

	private static bool IsDisabledRegion(JsonElement node)
		=> Property(node, UiComponentProperties.Modifiers) is { ValueKind: JsonValueKind.Object } modifiers &&
			modifiers.TryGetProperty(UiComponentModifiers.Disabled, out var disabled) &&
			disabled.ValueKind == JsonValueKind.True;

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
		=> node.TryGetProperty("properties", out var properties) &&
			properties.ValueKind == JsonValueKind.Object &&
			properties.TryGetProperty(key, out var value)
				? value
				: null;
}
