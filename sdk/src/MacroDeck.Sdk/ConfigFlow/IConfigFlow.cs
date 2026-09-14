namespace MacroDeck.Sdk.ConfigFlow;

/// <summary>A single configuration session. Implementations may keep per-session state in instance fields.</summary>
public interface IConfigFlow
{
	/// <summary>Returns the first step.</summary>
	Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken);

	/// <summary>
	/// Processes submitted values and advances, re-displays the step with errors, or completes the flow. Secret values are provided as plaintext for validation.
	/// </summary>
	/// <remarks>
	/// The user can go back, so <paramref name="stepId"/> may name an earlier step than the one returned last. Dispatch on
	/// <paramref name="stepId"/> and rebuild that step's state instead of assuming steps arrive in order; values collected by the
	/// steps after it are no longer in <paramref name="input"/>.
	/// </remarks>
	Task<ConfigFlowResult> SubmitAsync(
		string stepId,
		IReadOnlyDictionary<string, object?> input,
		IConfigFlowContext context,
		CancellationToken cancellationToken);
}
