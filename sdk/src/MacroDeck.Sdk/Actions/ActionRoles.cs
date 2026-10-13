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

	/// <summary>
	/// Runs a Macro Deck script on this host. The action takes a required dynamic-choice
	/// <c>scriptId</c> parameter, the script to run. Each option may carry the metadata
	/// <c>scriptInputs</c>, the script's input declarations as a JSON array of
	/// <see cref="Scripts.ScriptInput" />, and <c>runsOnWidget</c> set to <c>"true"</c> when the script
	/// acts on a widget. The action editor then adds one <c>input:&lt;name&gt;</c> parameter per declared
	/// input and, for a widget script, a <c>widget</c> parameter naming the target widget; the executor
	/// receives them like any other parameter.
	/// </summary>
	public const string RunScript = "run-script";

	/// <summary>
	/// Runs a Macro Deck script on another Macro Deck instance. The action takes the parameters of
	/// <see cref="RunScript" /> except <c>widget</c>, plus an optional dynamic-choice <c>instance</c>
	/// parameter, the instance to run it on, which the editor passes as a current parameter when it lists
	/// the scripts. A widget script cannot target a widget remotely, and the editor says so.
	/// </summary>
	public const string RunRemoteScript = "run-remote-script";
}
