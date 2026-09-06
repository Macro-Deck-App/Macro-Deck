using MacroDeck.Localization;

namespace MacroDeckHost.Application.Actions;

public enum FlowExecutionStatus
{
	Succeeded,
	PartiallyFailed,
	Failed,
	Cancelled
}

public sealed class FlowExecutionResult
{
	public required Guid ExecutionId { get; init; }
	public required FlowExecutionStatus Status { get; init; }
	public long DurationMs { get; init; }

	public int MatchedFlows { get; init; }

	public IReadOnlyList<ActionExecutionOutcome> Actions { get; init; } = [];

	/// <summary>
	/// The declared script input names for which the caller supplied a value and that value was accepted.
	/// An input that fell back to its own declared default is not included, even though the run used a
	/// value for it. A caller compares this against what it supplied to tell "my value was used" from
	/// "silently ignored" (e.g. dropped in transit, or by a host that does not know inputs).
	/// </summary>
	public IReadOnlyList<string> AppliedInputs { get; init; } = [];

	public string? ErrorCode { get; init; }
	public LocalizedText ErrorMessage { get; init; }
}
