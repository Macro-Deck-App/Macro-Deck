using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Variables;
using Serilog;

namespace MacroDeckHost.Integrations.Http.Actions;

internal sealed class HttpVariableAccessor
{
	public IVariableApi? Current { get; set; }
}

internal static class HttpVariableWriter
{
	private static readonly ILogger _logger =
		IntegrationLog.For(HttpIntegration.IntegrationId, typeof(HttpVariableWriter));

	public static async Task<bool> WriteAsync(
		HttpVariableAccessor accessor,
		string variableName,
		VariableType type,
		object value)
	{
		var api = accessor.Current;
		if (api is null)
		{
			_logger.Warning("HTTP capture skipped: variable API unavailable");
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
			// A name the user already created by hand, or one whose existing type cannot hold this
			// value: the capture stops updating, which is the platform's contract, not a request failure.
			_logger.Warning(ex, "HTTP capture could not write variable '{Variable}'", variableName);
			return false;
		}
	}
}
