namespace MacroDeck.Sdk.Actions;

/// <summary>
/// The values of <see cref="IActionDefinition.Role" />. An open vocabulary: a host that does not know a
/// role treats the action as an ordinary one.
/// </summary>
public static class ActionRoles
{
	/// <summary>
	/// Launches an application. The action takes a required <c>path</c> parameter, the application to
	/// start, and an optional <c>arguments</c> parameter, the command line passed to it. When the user
	/// drops an application onto the deck, Macro Deck creates a button bound to an action with this role;
	/// it uses only the actions of built-in integrations for that, never a plugin's, so a plugin never
	/// receives the dropped path.
	/// </summary>
	public const string LaunchApplication = "launch-application";
}
