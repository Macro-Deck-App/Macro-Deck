namespace MacroDeck.Plugin.Protocol.Callbacks;

/// <summary>Arguments for <c>host.invoke</c> against <see cref="HostApis.Scripts"/>'s only operation,
/// <c>run</c>.</summary>
public sealed record ScriptsRunArguments
{
	public required string ScriptId { get; init; }

	public string? OriginClientId { get; init; }

	/// <summary>
	/// Values for the script's declared inputs, by input name. Optional: a caller that sends none - or a
	/// plugin built against a protocol build that predates this field - runs the script on its defaults.
	/// </summary>
	public IReadOnlyDictionary<string, object?>? Inputs { get; init; }

	/// <summary>
	/// The widget this run acts on, required when the script's <c>RunsOnWidget</c> is set. Optional: a
	/// caller running a script that does not run on a widget - or built against a protocol build that
	/// predates this field - sends nothing.
	/// </summary>
	public string? OwnerWidgetId { get; init; }
}
