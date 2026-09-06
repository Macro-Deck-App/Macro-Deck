using MacroDeckHost.Application.Actions;

namespace MacroDeckHost.Application.Scripts;

public interface IScriptRunner
{
	Task<FlowExecutionResult> RunAsync(
		Guid scriptId,
		string? originClientId,
		CancellationToken cancellationToken,
		int inheritedDepth = 0,
		IReadOnlyDictionary<string, object?>? inputs = null,
		string? ownerWidgetId = null);
}
