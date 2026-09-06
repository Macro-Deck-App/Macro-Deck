using MacroDeck.Sdk.Ui;

namespace MacroDeck.Sdk.Actions;

public sealed class ActionExecutionContext
{
	public required IReadOnlyDictionary<string, object> Parameters { get; init; }

	/// <summary>
	/// Id of the UI client that triggered this execution, when it was initiated by a client press.
	/// Null for backend-initiated executions. Actions can use this to target only the originating
	/// client instead of broadcasting to every connected client.
	/// </summary>
	public string? OriginClientId { get; init; }

	/// <summary>
	/// Runtime interaction surface for asking the originating client a question (e.g. a track
	/// picker). Null when no client interaction is available (backend-initiated executions, tests).
	/// </summary>
	public IActionInteractions? Interactions { get; init; }

	/// <summary>
	/// Runtime surface for opening a Macro Deck UI modal on the originating client and, optionally,
	/// waiting for its answer. Null when no client interaction is available (backend-initiated
	/// executions, tests), which an action must handle rather than assume away - a modal is a question,
	/// and there is not always somebody to ask.
	/// </summary>
	public IUiInteractions? Ui { get; init; }

	/// <summary>
	/// Id of the widget whose flow is running, when the flow belongs to one - a press trigger, a
	/// state change, or a widget's own event trigger. Null for a script, an automation and any other
	/// widget-less run, which is why an action that targets "this widget" must handle its absence
	/// rather than assume it.
	/// </summary>
	public string? OwnerWidgetId { get; init; }

	/// <summary>
	/// Cancellation token for the running flow. Long-running actions (key sequences, delays, network
	/// calls) should honour it so the execution can be aborted. Defaults to <see cref="CancellationToken.None"/>.
	/// </summary>
	public CancellationToken CancellationToken { get; init; }

	/// <summary>
	/// How many script hops this run is already nested behind - local "Run Script" calls and, for a
	/// delegated run, the hops taken on the hosts before this one. Zero for a top-level run. An action
	/// that hands work to another Macro Deck passes this on, incremented, so a cycle across two hosts is
	/// bounded by the same budget a cycle within one host is.
	/// </summary>
	public int CallDepth { get; init; }
}
