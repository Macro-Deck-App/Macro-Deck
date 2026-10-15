using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Variables;
using Serilog;

namespace MacroDeckHost.Integrations.Streamerbot.Actions;

internal sealed class StreamerbotVariableAccessor
{
	public IVariableApi? Current { get; set; }

	public IUserVariableApi? UserVariables { get; set; }
}

internal static class StreamerbotVariableWriter
{
	private static readonly ILogger _logger =
		IntegrationLog.For(StreamerbotIntegration.IntegrationId, typeof(StreamerbotVariableWriter));

	public static Task WriteAsync(
		StreamerbotVariableAccessor accessor,
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
