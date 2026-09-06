using MacroDeck.Localization;

namespace MacroDeck.Sdk.Actions;

public enum ActionResultStatus
{
	Succeeded,

	/// <summary>
	/// The provider took the request but has not confirmed it. Only for operations the external API
	/// genuinely cannot confirm - where it can, poll until it does and report the real outcome.
	/// </summary>
	Accepted,

	Failed
}

/// <summary>
/// What an action actually did. An executor that cannot do its job returns
/// <see cref="Failed"/> rather than returning quietly: the flow engine records the failure and the
/// originating client is told, instead of a press that did nothing reporting success.
///
/// Immutable, because <see cref="Success"/> and <see cref="SucceededTask"/> hand out shared instances.
/// </summary>
public sealed class ActionResult
{
	private static readonly ActionResult _success = new() { Status = ActionResultStatus.Succeeded };

	public required ActionResultStatus Status { get; init; }

	/// <summary>Stable, machine-readable failure reason - see <see cref="ActionErrorCodes"/>.</summary>
	public string? ErrorCode { get; init; }

	/// <summary>
	/// Shown to the user as-is, so it must read as an explanation and must not carry provider
	/// internals, tokens or paths. Only set when <see cref="Status"/> is <see cref="ActionResultStatus.Failed"/>.
	/// </summary>
	public LocalizedText ErrorMessage { get; init; }

	/// <summary>Informational detail, e.g. what an <see cref="ActionResultStatus.Accepted"/> run is waiting on.</summary>
	public LocalizedText Message { get; init; }

	/// <summary>
	/// The stable id the action expects a state-provider widget to report after this successful run.
	/// Hosts may use it briefly while waiting for the provider to confirm the change.
	/// </summary>
	public string? ExpectedStateId { get; init; }

	public static ActionResult Success() => _success;

	public static ActionResult Success(string expectedStateId)
		=> new() { Status = ActionResultStatus.Succeeded, ExpectedStateId = expectedStateId };

	/// <summary>Cached completed task, so a synchronous executor stays a single statement.</summary>
	public static Task<ActionResult> SucceededTask { get; } = Task.FromResult(_success);

	public static ActionResult Accepted(LocalizedText message = default)
		=> new() { Status = ActionResultStatus.Accepted, Message = message };

	public static ActionResult Accepted(LocalizedText message, string expectedStateId)
		=> new() { Status = ActionResultStatus.Accepted, Message = message, ExpectedStateId = expectedStateId };

	public static ActionResult Failed(string code, LocalizedText message)
		=> new() { Status = ActionResultStatus.Failed, ErrorCode = code, ErrorMessage = message };
}
