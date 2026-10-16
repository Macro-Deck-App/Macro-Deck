using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Integrations.Meld.Protocol;
using Serilog;

namespace MacroDeckHost.Integrations.Meld.Actions;

internal static class MeldVariableWriter
{
	private static readonly ILogger _logger =
		IntegrationLog.For(MeldObjects.IntegrationId, typeof(MeldVariableWriter));

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
