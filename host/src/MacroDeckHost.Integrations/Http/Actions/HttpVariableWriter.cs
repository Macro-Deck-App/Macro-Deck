using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Integrations.Variables;
using Serilog;

namespace MacroDeckHost.Integrations.Http.Actions;

internal sealed class HttpVariableAccessor
{
	public IVariableApi? Current { get; set; }

	public IUserVariableApi? UserVariables { get; set; }
}

internal static class HttpVariableWriter
{
	private static readonly ILogger _logger =
		IntegrationLog.For(HttpIntegration.IntegrationId, typeof(HttpVariableWriter));

	public static Task<bool> WriteAsync(
		HttpVariableAccessor accessor,
		string variableName,
		string? ownerWidgetId,
		VariableType type,
		object value)
		=> ActionVariableTarget.WriteAsync(accessor.UserVariables,
			accessor.Current,
			variableName,
			ownerWidgetId,
			type,
			value,
			_logger);
}
