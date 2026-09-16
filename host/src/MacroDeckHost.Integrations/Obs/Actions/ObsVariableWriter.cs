using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Integrations.Variables;
using Serilog;

namespace MacroDeckHost.Integrations.Obs.Actions;

internal static class ObsVariableWriter
{
	private static readonly ILogger _logger =
		IntegrationLog.For(ObsIntegration.IntegrationId, typeof(ObsVariableWriter));

	public static Task<bool> WriteAsync(
		VariableApiAccessor accessor,
		string variableName,
		string? ownerWidgetId,
		VariableType type,
		object value)
		=> ActionVariableTarget.WriteAsync(accessor.UserVariables,
			accessor.Current,
			variableName,
			ownerWidgetId,
			type,
			value,
			_logger);
}
