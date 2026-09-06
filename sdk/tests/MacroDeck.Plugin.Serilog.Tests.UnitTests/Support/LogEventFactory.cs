using Serilog.Events;
using Serilog.Parsing;

namespace MacroDeck.Plugin.Serilog.Tests.UnitTests.Support;

/// <summary>Builds a bare <see cref="LogEvent" /> with an explicit timestamp and no properties, for
/// tests that only care about queueing and ordering, not about template rendering.</summary>
internal static class LogEventFactory
{
	private static readonly MessageTemplateParser _parser = new();

	public static LogEvent Create(DateTimeOffset timestamp, LogEventLevel level, string message)
		=> new(timestamp, level, null, _parser.Parse(message), []);
}
