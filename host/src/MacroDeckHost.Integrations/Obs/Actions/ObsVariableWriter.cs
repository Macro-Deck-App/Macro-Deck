using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Variables;
using Serilog;

namespace MacroDeckHost.Integrations.Obs.Actions;

internal static class ObsVariableWriter
{
	private static readonly ILogger _logger =
		IntegrationLog.For(ObsIntegration.IntegrationId, typeof(ObsVariableWriter));

	public static Task<bool> WriteAsync(
		VariableApiAccessor accessor,
		string variableName,
		UserVariableContext variableContext,
		VariableType type,
		object value)
	{
		return ActionVariableTarget.WriteAsync(accessor.UserVariables,
			accessor.Current,
			variableName,
			variableContext,
			type,
			value,
			_logger);
	}
}
