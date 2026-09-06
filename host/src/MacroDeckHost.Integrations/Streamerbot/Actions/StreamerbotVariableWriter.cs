using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Variables;
using Serilog;

namespace MacroDeckHost.Integrations.Streamerbot.Actions;

internal sealed class StreamerbotVariableAccessor
{
	public IVariableApi? Current { get; set; }
}

internal static class StreamerbotVariableWriter
{
	private static readonly ILogger _logger =
		IntegrationLog.For(StreamerbotIntegration.IntegrationId, typeof(StreamerbotVariableWriter));

	public static async Task WriteAsync(
		StreamerbotVariableAccessor accessor,
		string variableName,
		VariableType type,
		object value)
	{
		var api = accessor.Current;
		if (api is null)
		{
			_logger.Warning("Streamer.bot get action skipped: variable API unavailable");
			return;
		}

		try
		{
			var handle = await api.GetByNameAsync(variableName) ?? await api.CreateAsync(variableName, type);
			await api.SetValueAsync(handle.Id, value);
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "Streamer.bot get action could not write variable '{Variable}'", variableName);
		}
	}
}
