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
