using Serilog;
using Serilog.Configuration;

namespace MacroDeckHost.Logging;

public static class RedactingSinkExtensions
{
	public static LoggerConfiguration Redacted(
		this LoggerSinkConfiguration sinkConfiguration,
		Action<LoggerSinkConfiguration> configureSinks)
	{
		ArgumentNullException.ThrowIfNull(sinkConfiguration);
		ArgumentNullException.ThrowIfNull(configureSinks);

		return sinkConfiguration.Sink(LoggerSinkConfiguration.Wrap(sink => new RedactingSink(sink), configureSinks));
	}
}
