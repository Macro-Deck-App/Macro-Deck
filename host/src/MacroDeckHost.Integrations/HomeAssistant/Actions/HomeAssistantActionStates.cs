using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Integrations.HomeAssistant.Actions;

/// <summary>
/// Reads an entity's on/off state for a state-provider action. Everything comes from the entity cache
/// the connection keeps up to date from Home Assistant's <c>state_changed</c> subscription, so a
/// button following an entity costs no request of its own.
/// </summary>
internal static class HomeAssistantActionStates
{
	private const string On = "on";
	private const string Off = "off";

	/// <summary>
	/// On/off for one entity, or for a set of them: an action can target several entities at once, and
	/// the button is "on" only while they all are, which is what the user sees on the wall.
	/// </summary>
	public static ActionStateSnapshot? Snapshot(HomeAssistantConnection? connection, IReadOnlyList<string> entityIds)
	{
		if (entityIds.Count == 0)
		{
			return null;
		}

		if (connection is null || !connection.IsConnected)
		{
			return ActionStates.Snapshot(ActionStates.OnOff, null);
		}

		bool? all = null;
		foreach (var entityId in entityIds)
		{
			var state = connection.Entity(entityId)?.State;
			var on = state switch
			{
				On => true,
				Off => false,
				_ => (bool?)null
			};

			if (on is null)
			{
				return ActionStates.Snapshot(ActionStates.OnOff, null);
			}

			all = all is null ? on : all.Value && on.Value;
		}

		return ActionStates.Snapshot(ActionStates.OnOff, all);
	}
}
