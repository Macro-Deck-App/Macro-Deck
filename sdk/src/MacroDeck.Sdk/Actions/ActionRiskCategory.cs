namespace MacroDeck.Sdk.Actions;

/// <summary>
/// What an action can do outside Macro Deck that a person would want to approve before an automated
/// caller, such as an AI client connected over MCP, runs it. Declare the category that fits best; an action
/// that only changes Macro Deck itself or reads state keeps <see cref="None" />.
/// </summary>
public enum ActionRiskCategory
{
	/// <summary>No effect beyond what the action's name says; the default.</summary>
	None = 0,

	/// <summary>Runs a script or other code, here or on another machine.</summary>
	Scripts = 1,

	/// <summary>Starts, stops or opens programs, or runs a command.</summary>
	Programs = 2,

	/// <summary>Sends keystrokes, mouse input or text to the focused application or a device.</summary>
	Input = 3,

	/// <summary>Sends requests to an address the action's parameters choose.</summary>
	Network = 4,

	/// <summary>Opens, reads or writes files and folders.</summary>
	Files = 5,

	/// <summary>Shuts down, restarts, suspends or reboots a computer or device.</summary>
	Power = 6
}
