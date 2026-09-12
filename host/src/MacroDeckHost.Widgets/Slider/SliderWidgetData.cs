using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Widgets.Configuration;

namespace MacroDeckHost.Widgets.Slider;

public sealed record SliderWidgetData
{
	public string? Label { get; init; }

	public bool IsVertical { get; init; }

	public string? Color { get; init; }

	public string? LabelColor { get; init; }

	public string? BackgroundColor { get; init; }

	/// <summary>The optional icon shown inside the slider body. Reads the typed <c>icon</c> shape first
	/// and a legacy bare <c>iconId</c> second, tolerant forever.</summary>
	public WidgetIconReference? Icon { get; init; }

	public bool ShowLabel { get; init; } = true;

	public bool ShowValue { get; init; }

	/// <summary>Name of the variable the slider displays and writes, or <c>null</c> when it is unbound. A
	/// pure function of the stored key - never of whether that variable happens to exist right now -
	/// because the tree's declared event set must not change under a live session.</summary>
	public string? ValueVariable { get; init; }

	/// <summary>Lower bound of the range, used only when the bound variable declares no minimum of its
	/// own. The fallback is per field: a variable declaring a maximum but no minimum takes its maximum
	/// from the variable and its minimum from here, because a numeric user variable has no provider to
	/// declare any bound and an all-or-nothing rule would leave its slider undraggable.</summary>
	public double Min { get; init; }

	/// <summary>Upper bound of the range, used only when the bound variable declares no maximum of its
	/// own. See <see cref="Min" /> for why the fallback is per field.</summary>
	public double Max { get; init; } = 100;

	/// <summary>Grid granularity, used only when the bound variable declares no step of its own. See
	/// <see cref="Min" /> for why the fallback is per field.</summary>
	public double Step { get; init; } = 1;

	public bool HasDoublePressFlow { get; init; }

	public static SliderWidgetData Parse(JsonElement data)
	{
		if (data.ValueKind != JsonValueKind.Object)
		{
			return new SliderWidgetData();
		}

		return new SliderWidgetData
		{
			Label = Trimmed(ReadString(data, "label")),
			IsVertical = string.Equals(ReadString(data, "orientation"), "vertical", StringComparison.Ordinal),
			Color = WidgetColor.Normalize(ReadString(data, "color")),
			LabelColor = WidgetColor.Normalize(ReadString(data, "labelColor")),
			BackgroundColor = WidgetColor.Normalize(ReadString(data, "backgroundColor")),
			Icon = ReadIcon(data),
			ShowLabel = ReadBool(data, "showLabel") ?? true,
			ShowValue = ReadBool(data, "showValue") ?? false,
			ValueVariable = Trimmed(ReadString(data, "valueVariable")),
			Min = ReadDouble(data, "min") ?? 0,
			Max = ReadDouble(data, "max") ?? 100,
			Step = ReadDouble(data, "step") ?? 1,
			HasDoublePressFlow = HasFlowFor(data, WidgetTriggerTypes.DoublePress),
		};
	}

	private static bool HasFlowFor(JsonElement data, string triggerType)
		=> WidgetConfigJson.ReadFlows(data)
			.EnumerateArray()
			.Any(flow => flow.ValueKind == JsonValueKind.Object &&
				flow.TryGetProperty("triggerType", out var type) &&
				type.ValueKind == JsonValueKind.String &&
				string.Equals(type.GetString(), triggerType, StringComparison.OrdinalIgnoreCase) &&
				flow.TryGetProperty("children", out var children) &&
				children.ValueKind == JsonValueKind.Array &&
				children.EnumerateArray().Any(IsEnabledBlock));

	private static bool IsEnabledBlock(JsonElement block)
		=> block.ValueKind == JsonValueKind.Object &&
			!(block.TryGetProperty("disabled", out var disabled) && disabled.ValueKind == JsonValueKind.True);

	private static WidgetIconReference? ReadIcon(JsonElement data)
	{
		var icon = data.TryGetProperty("icon", out var iconElement) && iconElement.ValueKind == JsonValueKind.Object
			? JsonNode.Parse(iconElement.GetRawText())
			: null;

		return WidgetIconReference.Read(icon, Trimmed(ReadString(data, "iconId")));
	}

	private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

	private static string? ReadString(JsonElement data, string name)
		=> data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;

	private static bool? ReadBool(JsonElement data, string name)
		=> data.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
			? value.GetBoolean()
			: null;

	private static double? ReadDouble(JsonElement data, string name)
		=> data.TryGetProperty(name, out var value) &&
			value.ValueKind == JsonValueKind.Number &&
			value.TryGetDouble(out var number) &&
			double.IsFinite(number)
				? number
				: null;
}
