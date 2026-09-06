using System.Text.Json;

namespace MacroDeckHost.Widgets.ActionButton;

/// <summary>One action block found among this button's own <c>flows</c>, as much as the state/icon provider
/// offer needs to probe it: which integration/action it names, the id it can be adopted by, and its
/// currently-configured parameters. A local, read-only walk rather than a reference to
/// <c>MacroDeckHost.Application.Actions.ActionFlowJson</c>'s <c>ActionBlock</c> - that type is internal to
/// the Application assembly and not shared with Widgets.</summary>
internal sealed record ActionButtonFlowBlockInfo(
	string Id,
	string IntegrationId,
	string ActionId,
	string Label,
	IReadOnlyDictionary<string, object?> Parameters);

/// <summary>
/// Enumerates the action blocks in a button's own <c>flows</c> array, depth-first through nested children and
/// condition branches - enough to let the state/icon provider offer ask "does any block in this button's own
/// flows declare its own state or icon", the same question <c>GetActionProviderStatesRequestMessageHandler</c>
/// answers for one already-known block id.
/// </summary>
internal static class ActionButtonFlowBlocks
{
	public static IReadOnlyList<ActionButtonFlowBlockInfo> Enumerate(JsonElement flows)
	{
		var result = new List<ActionButtonFlowBlockInfo>();

		if (flows.ValueKind != JsonValueKind.Array)
		{
			return result;
		}

		foreach (var flow in flows.EnumerateArray())
		{
			if (flow.ValueKind == JsonValueKind.Object && flow.TryGetProperty("children", out var children))
			{
				CollectBlocks(children, result);
			}
		}

		return result;
	}

	private static void CollectBlocks(JsonElement blocks, List<ActionButtonFlowBlockInfo> result)
	{
		if (blocks.ValueKind != JsonValueKind.Array)
		{
			return;
		}

		foreach (var block in blocks.EnumerateArray())
		{
			if (block.ValueKind != JsonValueKind.Object)
			{
				continue;
			}

			var id = ReadString(block, "id");
			var integrationId = ReadString(block, "integrationId");
			var actionId = ReadString(block, "actionId");

			if (id is not null && integrationId is not null && actionId is not null)
			{
				result.Add(new ActionButtonFlowBlockInfo(id,
					integrationId,
					actionId,
					ReadString(block, "label") ?? string.Empty,
					ReadParameters(block)));
			}

			if (block.TryGetProperty("children", out var nested))
			{
				CollectBlocks(nested, result);
			}

			if (block.TryGetProperty("branches", out var branches) && branches.ValueKind == JsonValueKind.Array)
			{
				foreach (var branch in branches.EnumerateArray())
				{
					if (branch.ValueKind == JsonValueKind.Object &&
						branch.TryGetProperty("children", out var branchChildren))
					{
						CollectBlocks(branchChildren, result);
					}
				}
			}
		}
	}

	private static Dictionary<string, object?> ReadParameters(JsonElement block)
	{
		var result = new Dictionary<string, object?>(StringComparer.Ordinal);

		if (!block.TryGetProperty("parameters", out var parameters) || parameters.ValueKind != JsonValueKind.Array)
		{
			return result;
		}

		foreach (var parameter in parameters.EnumerateArray())
		{
			if (parameter.ValueKind != JsonValueKind.Object)
			{
				continue;
			}

			var name = ReadString(parameter, "name");

			if (name is null)
			{
				continue;
			}

			result[name] = parameter.TryGetProperty("value", out var value) ? ConvertValue(value) : null;
		}

		return result;
	}

	private static object? ConvertValue(JsonElement value) => value.ValueKind switch
	{
		JsonValueKind.String => value.GetString(),
		JsonValueKind.Number => value.TryGetInt64(out var integer) ? integer : value.GetDouble(),
		JsonValueKind.True => true,
		JsonValueKind.False => false,
		JsonValueKind.Object or JsonValueKind.Array => value.Clone(),
		_ => null,
	};

	private static string? ReadString(JsonElement obj, string name)
		=> obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;
}
