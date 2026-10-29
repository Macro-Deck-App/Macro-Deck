using System.Text.Json;
using MacroDeckHost.Application.Actions;

namespace MacroDeckHost.Application.Secrets.Approvals;

public sealed record FlowBlockParameter(string Name, string Type, JsonElement Value);

public sealed record FlowBlockContent(
	string? IntegrationId,
	string? ActionId,
	IReadOnlyList<FlowBlockParameter> Parameters)
{
	public bool HasDuplicateParameterNames
		=> Parameters.Select(parameter => parameter.Name).Distinct(StringComparer.Ordinal).Count() != Parameters.Count;

	public bool CarriesSecretReference
		=> Parameters.Any(parameter => DeclaredSecretWalker.ContainsReference(parameter.Value));

	public static FlowBlockContent FromJson(JsonElement block)
	{
		var (integrationId, actionId) = ActionFlowJson.ResolveActionIdentity(StringProperty(block, "integrationId"),
			StringProperty(block, "actionId"),
			StringProperty(block, "blockType"));

		var parameters = new List<FlowBlockParameter>();
		if (TryGetProperty(block, "parameters", out var stored) && stored.ValueKind == JsonValueKind.Array)
		{
			foreach (var parameter in stored.EnumerateArray())
			{
				if (parameter.ValueKind != JsonValueKind.Object)
				{
					continue;
				}

				var name = StringProperty(parameter, "name");
				if (string.IsNullOrWhiteSpace(name))
				{
					continue;
				}

				var value = TryGetProperty(parameter, "value", out var raw) ? raw.Clone() : default;
				parameters.Add(new FlowBlockParameter(name, StringProperty(parameter, "type") ?? string.Empty, value));
			}
		}

		return new FlowBlockContent(integrationId, actionId, parameters);
	}

	internal static string? StringProperty(JsonElement element, string name)
	{
		return TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;
	}

	internal static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
	{
		if (element.ValueKind == JsonValueKind.Object)
		{
			foreach (var property in element.EnumerateObject())
			{
				if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
				{
					value = property.Value;
					return true;
				}
			}
		}

		value = default;
		return false;
	}
}

public sealed record FlowBlockSnapshot(string Id, string Raw, FlowBlockContent Content);

public static class FlowSecretBlocks
{
	public static IReadOnlyList<FlowBlockSnapshot> FromWidgetData(string? widgetData)
	{
		return WidgetFlowsJson.TryExtract(widgetData, out var flows) ? FromFlows(flows) : [];
	}

	public static IReadOnlyList<FlowBlockSnapshot> FromFlows(string? flowsJson)
	{
		if (string.IsNullOrWhiteSpace(flowsJson) || !flowsJson.Contains("$secret", StringComparison.Ordinal))
		{
			return [];
		}

		try
		{
			using var document = JsonDocument.Parse(flowsJson);
			var blocks = new List<FlowBlockSnapshot>();
			if (document.RootElement.ValueKind == JsonValueKind.Array)
			{
				foreach (var flow in document.RootElement.EnumerateArray())
				{
					Collect(flow, blocks, true);
				}
			}

			return blocks;
		}
		catch (JsonException)
		{
			return [];
		}
	}

	public static IReadOnlyList<FlowBlockSnapshot> ActionBlocksOfFlows(string? flowsJson)
	{
		if (string.IsNullOrWhiteSpace(flowsJson))
		{
			return [];
		}

		try
		{
			using var document = JsonDocument.Parse(flowsJson);
			var blocks = new List<FlowBlockSnapshot>();
			if (document.RootElement.ValueKind == JsonValueKind.Array)
			{
				foreach (var flow in document.RootElement.EnumerateArray())
				{
					Collect(flow, blocks, false);
				}
			}

			return blocks;
		}
		catch (JsonException)
		{
			return [];
		}
	}

	public static IReadOnlyList<FlowBlockSnapshot> ActionBlocksOfWidgetData(string? widgetData)
	{
		return WidgetFlowsJson.TryExtract(widgetData, out var flows) ? ActionBlocksOfFlows(flows) : [];
	}

	public static FlowBlockSnapshot? Parse(string raw)
	{
		try
		{
			using var document = JsonDocument.Parse(raw);
			var block = document.RootElement;
			return new FlowBlockSnapshot(FlowBlockContent.StringProperty(block, "id") ?? string.Empty,
				raw,
				FlowBlockContent.FromJson(block));
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private static void Collect(JsonElement node, List<FlowBlockSnapshot> blocks, bool secretCarryingOnly)
	{
		if (node.ValueKind != JsonValueKind.Object)
		{
			return;
		}

		if (ActionFlowJson.IsActionBlock(FlowBlockContent.StringProperty(node, "type"),
			FlowBlockContent.StringProperty(node, "integrationId"),
			FlowBlockContent.StringProperty(node, "blockType")))
		{
			var content = FlowBlockContent.FromJson(node);
			if (!secretCarryingOnly || content.CarriesSecretReference)
			{
				blocks.Add(new FlowBlockSnapshot(FlowBlockContent.StringProperty(node, "id") ?? string.Empty,
					node.GetRawText(),
					content));
			}
		}

		if (FlowBlockContent.TryGetProperty(node, "children", out var children)
			&& children.ValueKind == JsonValueKind.Array)
		{
			foreach (var child in children.EnumerateArray())
			{
				Collect(child, blocks, secretCarryingOnly);
			}
		}

		if (FlowBlockContent.TryGetProperty(node, "branches", out var branches)
			&& branches.ValueKind == JsonValueKind.Array)
		{
			foreach (var branch in branches.EnumerateArray())
			{
				Collect(branch, blocks, secretCarryingOnly);
			}
		}
	}
}
