using MacroDeckHost.Application.Plugins.Trust;
using Serilog;
using Serilog.Events;

namespace MacroDeckHost.Infrastructure.Plugins.Trust;

internal static class PluginTrustLog
{
	public static void Evaluated(ILogger logger, string subject, PluginTrustVerdict verdict, string? certificateId)
	{
		// Every launch attempt re-evaluates trust, so the common Trusted outcome must not flood the
		// Information log on every restart; anything else is worth seeing at the default level.
		var level = verdict == PluginTrustVerdict.Trusted ? LogEventLevel.Debug : LogEventLevel.Information;

		logger.Write(level,
			"Plugin '{Subject}' evaluated as {Verdict} (certificate {CertificateId}).",
			subject,
			verdict,
			certificateId ?? "none");
	}

	public static void RevocationCheckFailed(ILogger logger, string certificateId, Exception exception)
		=> logger.Warning(exception, "Revocation check for certificate '{CertificateId}' failed.", certificateId);
}
