namespace MacroDeck.Sdk.ConfigFlow;

/// <summary>A single configuration session. Implementations may keep per-session state in instance fields.</summary>
public interface IConfigFlow
{
	/// <summary>Returns the first step.</summary>
	Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken);

	/// <summary>
	/// Processes submitted values and advances, re-displays the step with errors, or completes the flow. Secret values are provided as plaintext for validation.
	/// </summary>
	Task<ConfigFlowResult> SubmitAsync(
		string stepId,
		IReadOnlyDictionary<string, object?> input,
		IConfigFlowContext context,
		CancellationToken cancellationToken);
}
