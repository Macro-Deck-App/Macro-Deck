using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Variables;
using Serilog;

namespace MacroDeckHost.Integrations.Twitch.Actions;

internal static class TwitchVariableWriter
{
	private static readonly ILogger _logger =
		IntegrationLog.For(TwitchIntegration.IntegrationId).ForContext(typeof(TwitchVariableWriter));

	public static async Task WriteAsync(IVariableApi? api, string variableName, VariableType type, object value)
	{
		if (api is null)
		{
			_logger.Warning("Twitch action could not write '{Variable}': variable API unavailable", variableName);
			return;
		}

		try
		{
			var handle = await api.GetByNameAsync(variableName) ?? await api.CreateAsync(variableName, type);
			await api.SetValueAsync(handle.Id, value);
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "Twitch action could not write variable '{Variable}'", variableName);
		}
	}
}
