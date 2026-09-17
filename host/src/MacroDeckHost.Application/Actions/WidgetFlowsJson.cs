using System.Text.Json;

namespace MacroDeckHost.Application.Actions;

public static class WidgetFlowsJson
{
	public static string ToSource(string? flowsJson) =>
		JsonSerializer.Serialize(new FlowsEnvelope(flowsJson ?? string.Empty));

	public static bool TryExtract(string? widgetData, out string flowsJson)
	{
		flowsJson = string.Empty;
		if (string.IsNullOrWhiteSpace(widgetData))
		{
			return false;
		}

		try
		{
			using var document = JsonDocument.Parse(widgetData);
			if (document.RootElement.ValueKind != JsonValueKind.Object ||
				!TryGetProperty(document.RootElement, "flows", out var flows))
			{
				return false;
			}

			var json = flows.ValueKind == JsonValueKind.String ? flows.GetString() : flows.GetRawText();
			if (string.IsNullOrWhiteSpace(json))
			{
				return false;
			}

			flowsJson = json;
			return true;
		}
		catch (JsonException)
		{
			return false;
		}
	}

	public static bool HasRunnableFlow(string? widgetData, string triggerType)
	{
		if (!TryExtract(widgetData, out var flowsJson))
		{
			return false;
		}

		try
		{
			using var document = JsonDocument.Parse(flowsJson);
			return HasRunnableFlow(document.RootElement, triggerType);
		}
		catch (JsonException)
		{
			return false;
		}
	}

	public static bool HasRunnableFlow(JsonElement flows, string triggerType)
		=> flows.ValueKind == JsonValueKind.Array &&
			flows.EnumerateArray()
				.Any(flow => flow.ValueKind == JsonValueKind.Object &&
					TryGetProperty(flow, "triggerType", out var type) &&
					type.ValueKind == JsonValueKind.String &&
					string.Equals(type.GetString(), triggerType, StringComparison.OrdinalIgnoreCase) &&
					TryGetProperty(flow, "children", out var children) &&
					children.ValueKind == JsonValueKind.Array &&
					children.EnumerateArray().Any(IsEnabledBlock));

	private static bool IsEnabledBlock(JsonElement block)
		=> block.ValueKind == JsonValueKind.Object &&
			!(block.TryGetProperty("disabled", out var disabled) && disabled.ValueKind == JsonValueKind.True);

	/// <summary>The distinct trigger types the widget has a flow for, in the order they are stored.</summary>
	public static IReadOnlyList<string> TriggerTypes(string? widgetData)
	{
		if (!TryExtract(widgetData, out var flowsJson))
		{
			return [];
		}

		try
		{
			using var document = JsonDocument.Parse(flowsJson);
			if (document.RootElement.ValueKind != JsonValueKind.Array)
			{
				return [];
			}

			var types = new List<string>();
			foreach (var flow in document.RootElement.EnumerateArray())
			{
				if (flow.ValueKind != JsonValueKind.Object ||
					!TryGetProperty(flow, "triggerType", out var triggerType) ||
					triggerType.ValueKind != JsonValueKind.String)
				{
					continue;
				}

				var value = triggerType.GetString();
				if (!string.IsNullOrEmpty(value) && !types.Contains(value, StringComparer.OrdinalIgnoreCase))
				{
					types.Add(value);
				}
			}

			return types;
		}
		catch (JsonException)
		{
			return [];
		}
	}

	private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement property)
	{
		foreach (var jsonProperty in element.EnumerateObject())
		{
			if (string.Equals(jsonProperty.Name, propertyName, StringComparison.OrdinalIgnoreCase))
			{
				property = jsonProperty.Value;
				return true;
			}
		}

		property = default;
		return false;
	}

	private sealed record FlowsEnvelope(string Flows);
}
