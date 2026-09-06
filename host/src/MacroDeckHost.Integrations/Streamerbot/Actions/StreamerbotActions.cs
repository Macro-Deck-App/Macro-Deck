using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Integrations.Streamerbot.Actions;

internal static class StreamerbotActions
{
	public static IReadOnlyList<IActionDefinition> Create(
		Func<StreamerbotConnection?> resolver,
		StreamerbotVariableAccessor variables) =>
	[
		new DoActionActionDefinition(resolver),
		new ExecuteCodeTriggerActionDefinition(resolver),
		new SendChatMessageActionDefinition(resolver),
		new GetGlobalVariableActionDefinition(resolver, variables)
	];
}
