using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Variables;
using Serilog;

namespace MacroDeckHost.Integrations.StreamlabsDesktop.Actions;

internal static class StreamlabsVariableWriter
{
	private static readonly ILogger _logger =
		IntegrationLog.For(StreamlabsDesktopIntegration.IntegrationId, typeof(StreamlabsVariableWriter));

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
