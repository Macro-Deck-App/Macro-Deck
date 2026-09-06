using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Variables;
using Serilog;

namespace MacroDeckHost.Integrations.HomeAssistant.Actions;

internal sealed class HomeAssistantVariableAccessor
{
	public IVariableApi? Current { get; set; }
}

internal static class HomeAssistantVariableWriter
{
	private static readonly ILogger _logger =
		IntegrationLog.For(HomeAssistantIntegration.IntegrationId, typeof(HomeAssistantVariableWriter));

	public static async Task WriteAsync(
		HomeAssistantVariableAccessor accessor,
		string variableName,
		VariableType type,
		object value)
	{
		var api = accessor.Current;
		if (api is null)
		{
			_logger.Warning("Home Assistant get action skipped: variable API unavailable");
			return;
		}

		try
		{
			var handle = await api.GetByNameAsync(variableName) ?? await api.CreateAsync(variableName, type);
			await api.SetValueAsync(handle.Id, value);
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "Home Assistant get action could not write variable '{Variable}'", variableName);
		}
	}
}
