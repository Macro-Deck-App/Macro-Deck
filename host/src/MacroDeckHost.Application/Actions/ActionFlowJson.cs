using System.Text.Json;

namespace MacroDeckHost.Application.Actions;

/// <summary>
/// Parses the <c>flows</c> array stored in a widget's <c>Data</c>. Extracted from <see cref="FlowExecutor" />
/// so it and anything else that needs to locate a block within a widget's own flows (state-provider
/// resolution) share one parser and can never drift apart.
/// </summary>
internal static class ActionFlowJson
{
	private static readonly JsonSerializerOptions _jsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	public static List<ActionFlow> ParseFlows(string? widgetData)
	{
		if (string.IsNullOrWhiteSpace(widgetData))
		{
			return [];
		}

		using var document = JsonDocument.Parse(widgetData);
		if (!TryGetProperty(document.RootElement, "flows", out var flowsElement))
		{
			return [];
		}

		var flowsJson = flowsElement.ValueKind == JsonValueKind.String
			? flowsElement.GetString()
			: flowsElement.GetRawText();

		if (string.IsNullOrWhiteSpace(flowsJson))
		{
			return [];
		}

		return JsonSerializer.Deserialize<List<ActionFlow>>(flowsJson, _jsonOptions) ?? [];
	}

	/// <summary>Depth-first search through every flow's blocks, including inside condition branches.</summary>
	public static bool TryFindBlock(IEnumerable<ActionFlow> flows, string blockId, out ActionBlock? block)
	{
		foreach (var flow in flows)
		{
			if (TryFindBlock(flow.Children, blockId, out block))
			{
				return true;
			}
		}

		block = null;
		return false;
	}

	private static bool TryFindBlock(IEnumerable<ActionBlock> blocks, string blockId, out ActionBlock? block)
	{
		foreach (var candidate in blocks)
		{
			if (string.Equals(candidate.Id, blockId, StringComparison.Ordinal))
			{
				block = candidate;
				return true;
			}

			if (TryFindBlock(candidate.Children, blockId, out block))
			{
				return true;
			}

			if (candidate.Branches is not null)
			{
				foreach (var branch in candidate.Branches)
				{
					if (TryFindBlock(branch.Children, blockId, out block))
					{
						return true;
					}
				}
			}
		}

		block = null;
		return false;
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
}

internal sealed class ActionFlow
{
	public string TriggerId { get; set; } = string.Empty;
	public string TriggerType { get; set; } = string.Empty;
	public string? TriggerLabel { get; set; }
	public List<ActionBlock> Children { get; set; } = [];
}

internal sealed class ActionBlock
{
	public string Id { get; set; } = string.Empty;
	public string Type { get; set; } = string.Empty;
	public string BlockType { get; set; } = string.Empty;
	public string Label { get; set; } = string.Empty;
	public string? IntegrationId { get; set; }
	public string? ActionId { get; set; }
	public List<ActionBlockParameter> Parameters { get; set; } = [];

	public bool Disabled { get; set; }

	public JsonElement Condition { get; set; }

	public List<ActionBlock> Children { get; set; } = [];

	public List<ActionBranch>? Branches { get; set; }
}

internal sealed class ActionBranch
{
	public string Id { get; set; } = string.Empty;

	public string Kind { get; set; } = "if";

	public JsonElement Condition { get; set; }

	public List<ActionBlock> Children { get; set; } = [];
}

internal sealed class ActionBlockParameter
{
	public string Name { get; set; } = string.Empty;
	public string Type { get; set; } = string.Empty;
	public string Label { get; set; } = string.Empty;
	public JsonElement Value { get; set; }
}
