using Microsoft.Extensions.Logging;

namespace MacroDeckHost.Application.Plugins.Compatibility;

internal static partial class PluginCompatibilityLog
{
	[LoggerMessage(EventId = 5520,
		Level = LogLevel.Warning,
		Message = "Plugin '{PluginId}' reports compatibility state '{State}' ({FindingCount} finding(s), " +
			"evidence: {UsageSource}).")]
	public static partial void CompatibilityState(ILogger logger,
		string pluginId,
		string state,
		int findingCount,
		string usageSource);

	[LoggerMessage(EventId = 5521,
		Level = LogLevel.Warning,
		Message = "Plugin '{PluginId}' [{DiagnosticId}] {Subject}: {Guidance}")]
	public static partial void Finding(ILogger logger,
		string pluginId,
		string diagnosticId,
		string subject,
		string guidance);
}
