using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Variables;
using Serilog;

namespace MacroDeckHost.Integrations.Obs.Actions;

internal static class ObsVariableWriter
{
	private static readonly ILogger _logger =
		IntegrationLog.For(ObsIntegration.IntegrationId, typeof(ObsVariableWriter));

	public static async Task<bool> WriteAsync(
		VariableApiAccessor accessor,
		string variableName,
		VariableType type,
		object value)
	{
		var api = accessor.Current;
		if (api is null)
		{
			_logger.Warning("OBS get action skipped: variable API unavailable");
			return false;
		}

		try
		{
			var handle = await api.GetByNameAsync(variableName) ?? await api.CreateAsync(variableName, type);
			await api.SetValueAsync(handle.Id, value);
			return true;
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "OBS get action could not write variable '{Variable}'", variableName);
			return false;
		}
	}
}
