using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Variables;
using Serilog;

namespace MacroDeckHost.Integrations.HomeAssistant.Actions;

internal sealed class HomeAssistantVariableAccessor
{
	public IVariableApi? Current { get; set; }

	public IUserVariableApi? UserVariables { get; set; }
}

internal static class HomeAssistantVariableWriter
{
	private static readonly ILogger _logger =
		IntegrationLog.For(HomeAssistantIntegration.IntegrationId, typeof(HomeAssistantVariableWriter));

	public static Task WriteAsync(
		HomeAssistantVariableAccessor accessor,
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
