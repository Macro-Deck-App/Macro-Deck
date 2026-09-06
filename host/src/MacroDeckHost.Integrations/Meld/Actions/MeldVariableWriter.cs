using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Variables;
using Serilog;

namespace MacroDeckHost.Integrations.Meld.Actions;

internal static class MeldVariableWriter
{
	private static readonly ILogger _logger = IntegrationLog.For(MeldObjects.IntegrationId, typeof(MeldVariableWriter));

	public static async Task<bool> WriteAsync(
		VariableApiAccessor accessor,
		string variableName,
		VariableType type,
		object value)
	{
		var api = accessor.Current;
		if (api is null)
		{
			_logger.Warning("Meld Studio get action skipped: variable API unavailable");
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
			_logger.Warning(ex, "Meld Studio get action could not write variable '{Variable}'", variableName);
			return false;
		}
	}
}
