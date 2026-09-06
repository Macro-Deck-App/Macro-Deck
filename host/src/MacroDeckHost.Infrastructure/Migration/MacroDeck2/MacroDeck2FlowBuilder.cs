using System.Text.Json.Nodes;
using MacroDeck.Sdk.Migration;
using MacroDeckHost.Application.Migration;
using MacroDeckHost.Domain.Common;

namespace MacroDeckHost.Infrastructure.Migration.MacroDeck2;

/// <summary>
/// Turns Macro Deck 2's four action lists into Macro Deck 3 flows.
/// </summary>
/// <remarks>
/// The two applications do not agree on what a press is, so the mapping is deliberate rather than a
/// rename - and it deliberately favours what a button means over when it fires.
///
/// Macro Deck 2's client emits <c>ButtonPress</c> on mouse-down (its <c>onMouseDown</c> handler) and
/// <c>ButtonShortPressRelease</c> on release, so <c>Actions</c> is literally Macro Deck 3's
/// <c>onTouchStart</c>. It is nevertheless migrated to <c>onShortPress</c>: <c>Actions</c> is the list
/// every ordinary button uses and Macro Deck 2 presents it as "on press", so carrying it to the trigger
/// that means "pressed" is what a migrated deck should read as. The cost is stated rather than hidden -
/// <c>onShortPress</c> fires on release and is suppressed once a long press fired, so a button that has
/// both lists no longer runs its press actions during a long press.
///
/// <c>ActionsRelease</c> then goes to <c>onTouchEnd</c>, which is broader: Macro Deck 2 ran it only after a
/// short press, Macro Deck 3 runs it after every release. <c>ActionsLongPressRelease</c> has no
/// counterpart at all and would need that same trigger, so it is preserved as disabled blocks - visible
/// and editable, never firing at the wrong moment - and reported as a warning.
/// </remarks>
internal static class MacroDeck2FlowBuilder
{
	private const string ReleaseTrigger = WidgetTriggerTypes.TouchEnd;

	public static async Task<JsonArray> Build(
		MacroDeck2Button button,
		Func<MacroDeck2Action, Task<JsonObject>> translate)
	{
		var flows = new JsonArray();

		await AddFlow(flows, WidgetTriggerTypes.ShortPress, button.Actions, translate, disabled: false);
		await AddFlow(flows, WidgetTriggerTypes.LongPress, button.ActionsLongPress, translate, disabled: false);

		// Both release lists want the one trigger Macro Deck 3 has for a release, and two flows may never
		// share a trigger type - findFlowForTrigger takes the first match and the second would never run.
		// So they share one flow, with the long-press-release blocks switched off inside it.
		await AddReleaseFlow(flows, button, translate);

		return flows;
	}

	private static async Task AddReleaseFlow(
		JsonArray flows,
		MacroDeck2Button button,
		Func<MacroDeck2Action, Task<JsonObject>> translate)
	{
		if (button.ActionsRelease.Count == 0 && button.ActionsLongPressRelease.Count == 0)
		{
			return;
		}

		var children = new JsonArray();
		foreach (var action in button.ActionsRelease)
		{
			children.Add(await translate(action));
		}

		foreach (var action in button.ActionsLongPressRelease)
		{
			var block = await translate(action);
			block["disabled"] = JsonValue.Create(true);
			children.Add(block);
		}

		flows.Add(Flow(ReleaseTrigger, children));
	}

	private static async Task AddFlow(
		JsonArray flows,
		string triggerType,
		List<MacroDeck2Action> actions,
		Func<MacroDeck2Action, Task<JsonObject>> translate,
		bool disabled)
	{
		if (actions.Count == 0)
		{
			return;
		}

		var children = new JsonArray();
		foreach (var action in actions)
		{
			var block = await translate(action);
			if (disabled)
			{
				block["disabled"] = JsonValue.Create(true);
			}

			children.Add(block);
		}

		flows.Add(Flow(triggerType, children));
	}

	private static JsonObject Flow(string triggerType, JsonArray children)
		=> new()
		{
			["triggerId"] = JsonValue.Create(NewId()),
			["triggerType"] = JsonValue.Create(triggerType),
			["children"] = children
		};

	/// <summary>
	/// A block for an action no migrator claimed. It is deliberately left enabled: a disabled block is
	/// recorded as skipped rather than failed, which would let a migrated deck do nothing at all when
	/// pressed instead of saying that this step could not be brought across.
	/// </summary>
	public static JsonObject Placeholder(MacroDeck2Action action, string sourceApp)
	{
		var (typeName, assembly) = action.SplitType();
		var parameters = new JsonArray
		{
			Parameter("sourceApp", "text", sourceApp),
			Parameter("sourceAction", "text", string.IsNullOrEmpty(typeName) ? assembly : $"{typeName}, {assembly}"),
			Parameter("sourceConfiguration", "text", action.Configuration ?? string.Empty)
		};

		return Block(MigrationPlaceholder.IntegrationId,
			MigrationPlaceholder.ActionId,
			action.Name ?? typeName,
			parameters);
	}

	public static JsonObject Translated(ActionMigrationResult result)
	{
		var parameters = new JsonArray();
		foreach (var (name, value) in result.Parameters)
		{
			parameters.Add(new JsonObject
			{
				["name"] = JsonValue.Create(name),
				["value"] = JsonNode.Parse(value.GetRawText())
			});
		}

		return Block(result.IntegrationId, result.ActionId, result.Label, parameters);
	}

	private static JsonObject Block(string integrationId, string actionId, string label, JsonArray parameters)
		=> new()
		{
			["id"] = JsonValue.Create(NewId()),
			["type"] = JsonValue.Create("action"),
			["blockType"] = JsonValue.Create($"{integrationId}.{actionId}"),
			["label"] = JsonValue.Create(label),
			["color"] = JsonValue.Create("#3b82f6"),
			["integrationId"] = JsonValue.Create(integrationId),
			["actionId"] = JsonValue.Create(actionId),
			["parameters"] = parameters
		};

	private static JsonObject Parameter(string name, string type, string value)
		=> new()
		{
			["name"] = JsonValue.Create(name),
			["type"] = JsonValue.Create(type),
			["value"] = JsonValue.Create(value)
		};

	private static string NewId() => Guid.NewGuid().ToString("N")[..12];
}
