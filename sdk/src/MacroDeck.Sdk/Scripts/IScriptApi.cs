using MacroDeck.Sdk.Actions;

namespace MacroDeck.Sdk.Scripts;

/// <summary>Runs scripts stored by the host.</summary>
public interface IScriptApi
{
	/// <summary>Returns stored scripts for pickers.</summary>
	IReadOnlyList<Script> GetScripts();

	/// <summary>
	/// Runs a script to completion. Unknown or invalid IDs return <see cref="ActionResult.Failed"/>.
	/// </summary>
	/// <param name="inputs">
	/// Values for the script's declared <see cref="Script.Inputs" />, by input name; the script reads them
	/// as <c>vars.&lt;name&gt;</c>. A name the script does not declare is ignored. A declared input with no
	/// value here falls back to its default, and a required one without either fails the run before any
	/// block executes.
	/// </param>
	/// <param name="ownerWidgetId">
	/// The widget this run acts on, required when <see cref="Script.RunsOnWidget" /> is <c>true</c> - a
	/// widget-targeting action inside the script whose target is "this widget" resolves to it. A
	/// <see cref="Script.RunsOnWidget" /> script run with none, or with an id that does not resolve to a
	/// widget, fails before any block executes. Ignored when the script does not run on a widget.
	/// </param>
	Task<ActionResult> RunAsync(
		string scriptId,
		IReadOnlyDictionary<string, object?>? inputs = null,
		string? originClientId = null,
		string? ownerWidgetId = null,
		CancellationToken cancellationToken = default);
}
