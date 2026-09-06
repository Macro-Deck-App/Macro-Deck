namespace MacroDeck.Sdk.Scripts;

/// <summary>A stored script as offered to the user in a script picker.</summary>
public sealed class Script
{
	public required string Id { get; init; }

	public required string Name { get; init; }

	public string Description { get; init; } = string.Empty;

	/// <summary>
	/// The values this script expects its caller to supply, in declaration order. A script that declares
	/// none returns an empty list, never null.
	/// </summary>
	public IReadOnlyList<ScriptInput> Inputs { get; init; } = [];

	/// <summary>
	/// Whether this script runs on a widget: <c>RunAsync</c> needs an owner widget id to succeed, and
	/// inside the script's own flow, a widget-targeting action's <c>$self</c> resolves to that widget.
	/// Additive - a document written before this field existed deserializes with it defaulted to
	/// <c>false</c>.
	/// </summary>
	public bool RunsOnWidget { get; init; }
}
