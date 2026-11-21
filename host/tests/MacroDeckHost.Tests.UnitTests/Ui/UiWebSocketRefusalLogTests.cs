using System.Globalization;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Ui;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace MacroDeckHost.Tests.UnitTests.Ui;

public sealed class UiWebSocketRefusalLogTests
{
	private CapturingSink _sink = null!;
	private ManualTimeProvider _time = null!;
	private UiWebSocketRefusalLog _log = null!;

	[SetUp]
	public void SetUp()
	{
		_sink = new CapturingSink();
		_time = new ManualTimeProvider();
		var logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(_sink).CreateLogger();
		_log = new UiWebSocketRefusalLog(logger, _time);
	}

	[Test]
	public void A_refusal_a_window_can_run_into_is_a_warning_naming_the_reason()
	{
		_log.TicketRefused(8193, UiWebSocketTicketRefusal.ListenerMismatch);

		Assert.That(_sink.Events, Has.Count.EqualTo(1));
		Assert.That(_sink.Events[0].Level, Is.EqualTo(LogEventLevel.Warning));
		Assert.That(_sink.Events[0].RenderMessage(CultureInfo.InvariantCulture), Does.Contain("ListenerMismatch"));
	}

	[Test]
	public void Repeated_refusals_from_outside_add_one_warning_per_minute_and_kind()
	{
		for (var request = 0; request < 1000; request++)
		{
			_log.TicketRefused(8193, UiWebSocketTicketRefusal.UnknownOrUsed);
		}

		_log.TicketRefused(8193, UiWebSocketTicketRefusal.Expired);
		_time.Advance(TimeSpan.FromMinutes(1));
		_log.TicketRefused(8193, UiWebSocketTicketRefusal.UnknownOrUsed);

		Assert.That(_sink.Events.Count(e => e.Level == LogEventLevel.Warning), Is.EqualTo(3));
	}

	[Test]
	public void A_malformed_ticket_only_reaches_the_debug_log()
	{
		_log.TicketRefused(8193, UiWebSocketTicketRefusal.Malformed);

		Assert.That(_sink.Events.Select(e => e.Level), Is.EqualTo(new[] { LogEventLevel.Debug }));
	}

	private sealed class CapturingSink : ILogEventSink
	{
		public List<LogEvent> Events { get; } = [];

		public void Emit(LogEvent logEvent)
		{
			Events.Add(logEvent);
		}
	}
}
