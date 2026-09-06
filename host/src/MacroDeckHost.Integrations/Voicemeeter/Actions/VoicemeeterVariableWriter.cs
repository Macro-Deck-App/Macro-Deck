using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Variables;
using Serilog;

namespace MacroDeckHost.Integrations.Voicemeeter.Actions;

internal sealed class VoicemeeterVariableAccessor
{
	public IVariableApi? Current { get; set; }
}

internal static class VoicemeeterVariableWriter
{
	private static readonly ILogger _logger =
		IntegrationLog.For(VoicemeeterIntegration.IntegrationId, typeof(VoicemeeterVariableWriter));

	public static async Task WriteAsync(
		VoicemeeterVariableAccessor accessor,
		string variableName,
		VariableType type,
		object value)
	{
		var api = accessor.Current;
		if (api is null)
		{
			_logger.Warning("Voicemeeter read action skipped: the variable API is unavailable");
			return;
		}

		try
		{
			var handle = await api.GetByNameAsync(variableName) ?? await api.CreateAsync(variableName, type);
			await api.SetValueAsync(handle.Id, value);
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "Voicemeeter read action could not write variable '{Variable}'", variableName);
		}
	}
}
