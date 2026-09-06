using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Variables;
using Serilog;

namespace MacroDeckHost.Integrations.StreamlabsDesktop.Actions;

internal static class StreamlabsVariableWriter
{
	private static readonly ILogger _logger =
		IntegrationLog.For(StreamlabsDesktopIntegration.IntegrationId, typeof(StreamlabsVariableWriter));

	public static async Task<bool> WriteAsync(
		VariableApiAccessor accessor,
		string variableName,
		VariableType type,
		object value)
	{
		var api = accessor.Current;
		if (api is null)
		{
			_logger.Warning("Streamlabs Desktop get action skipped: variable API unavailable");
			return false;
		}

		try
		{
			var handle = await api.GetByNameAsync(variableName).ConfigureAwait(false) ??
				await api.CreateAsync(variableName, type).ConfigureAwait(false);
			await api.SetValueAsync(handle.Id, value).ConfigureAwait(false);
			return true;
		}
		catch (Exception ex)
		{
			_logger.Warning(ex,
				"Streamlabs Desktop get action could not write variable '{Variable}'",
				variableName);
			return false;
		}
	}
}
