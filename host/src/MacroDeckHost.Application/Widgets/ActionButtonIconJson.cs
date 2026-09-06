using System.Text.Json.Nodes;
using MacroDeckHost.Application.Actions;

namespace MacroDeckHost.Application.Widgets;

/// <summary>
/// Self-heals a dangling <c>iconProvider</c> assignment - the sibling of <c>stateProvider</c>'s own
/// self-healing, kept in its own type because an icon provider has no cached value and no manual backup to
/// restore (see <see cref="ActionButtonIconProvider" />'s remarks): removing the key is the entire repair.
/// Called from <see cref="ActionButtonStateJson.Normalize" /> so every host-controlled write self-heals
/// both capabilities together.
/// </summary>
public static class ActionButtonIconJson
{
	/// <summary>
	/// Drops <c>iconProvider</c> when the block it names is no longer among this button's own action-flow
	/// blocks - the same "deleted the owning action" case the editor detects and offers to fix live,
	/// applied here as the host-side backstop for a save the editor never had a chance to repair (a paste,
	/// an older client, a hand-edited body). A disabled block is left alone: unlike a missing one, it may
	/// come back, and rendering already falls back to the configured icon while it is disabled.
	/// </summary>
	public static bool Normalize(JsonObject data)
	{
		if (data["iconProvider"] is not JsonObject provider)
		{
			return false;
		}

		var blockId = provider["blockId"] is JsonValue blockIdValue && blockIdValue.TryGetValue<string>(out var id)
			? id
			: null;

		if (!string.IsNullOrEmpty(blockId) &&
			ActionFlowJson.TryFindBlock(ActionFlowJson.ParseFlows(data.ToJsonString()), blockId, out _))
		{
			return false;
		}

		data.Remove("iconProvider");
		return true;
	}
}
