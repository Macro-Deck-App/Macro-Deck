using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Integrations.Variables;
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
		string? ownerWidgetId,
		VariableType type,
		object value)
		=> ActionVariableTarget.WriteAsync(userVariables, api, variableName, ownerWidgetId, type, value, _logger);
}
