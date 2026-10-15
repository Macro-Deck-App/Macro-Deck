using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Variables;
using Serilog;

namespace MacroDeckHost.Integrations.Voicemeeter.Actions;

internal sealed class VoicemeeterVariableAccessor
{
	public IVariableApi? Current { get; set; }

	public IUserVariableApi? UserVariables { get; set; }
}

internal static class VoicemeeterVariableWriter
{
	private static readonly ILogger _logger =
		IntegrationLog.For(VoicemeeterIntegration.IntegrationId, typeof(VoicemeeterVariableWriter));

	public static Task WriteAsync(
		VoicemeeterVariableAccessor accessor,
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
