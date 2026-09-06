namespace MacroDeck.Sdk.Actions;

public interface IActionExecutor
{
	/// <summary>
	/// Runs the action and reports what happened. Returning <see cref="ActionResult.Success"/> claims
	/// the operation completed, so an executor that cannot reach its provider, lacks a permission or
	/// was handed unusable parameters returns <see cref="ActionResult.Failed"/> instead of returning
	/// quietly. Throwing is also fine - the flow engine records it as a failure and sanitizes the
	/// message - but a known condition deserves its own code.
	/// </summary>
	Task<ActionResult> ExecuteAsync(ActionExecutionContext context);
}
