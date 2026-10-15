using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Variables;
using Serilog;

namespace MacroDeckHost.Integrations.Twitch.Actions;

internal static class TwitchVariableWriter
{
	private static readonly ILogger _logger =
		IntegrationLog.For(TwitchIntegration.IntegrationId).ForContext(typeof(TwitchVariableWriter));

	public static Task WriteAsync(
		IUserVariableApi? userVariables,
		IVariableApi? api,
		string variableName,
		UserVariableContext variableContext,
		VariableType type,
		object value)
	{
		return ActionVariableTarget.WriteAsync(userVariables, api, variableName, variableContext, type, value, _logger);
	}
}
