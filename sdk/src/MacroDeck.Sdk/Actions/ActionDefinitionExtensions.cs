namespace MacroDeck.Sdk.Actions;

public static class ActionDefinitionExtensions
{
	/// <summary>Whether this action should be discovered on the operating system running now.</summary>
	public static bool RunsHere(this IActionDefinition action) =>
		action.Platforms.HasFlag(MacroDeckIntegrationAttribute.Current);
}
