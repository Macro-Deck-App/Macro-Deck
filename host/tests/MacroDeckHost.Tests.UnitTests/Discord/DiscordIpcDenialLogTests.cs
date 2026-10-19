using System.Globalization;
using MacroDeckHost.Integrations.Discord.Protocol;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace MacroDeckHost.Tests.UnitTests.Discord;

[TestFixture]
internal sealed class DiscordIpcDenialLogTests
{
	private CapturingSink _sink = null!;
	private DiscordIpcDenialLog _log = null!;

	[SetUp]
	public void SetUp()
	{
		_sink = new CapturingSink();
		var logger = new LoggerConfiguration()
			.MinimumLevel.Verbose()
			.WriteTo.Sink(_sink)
			.CreateLogger();
		_log = new DiscordIpcDenialLog(logger);
	}

	[Test]
	public void A_denial_is_logged_as_a_warning_with_what_the_probe_found()
	{
		_log.Record(TestDenials.Of(DiscordIpcDenialCause.OtherProgram, "SomeOverlay"));

		var message = _sink.Events.Single().RenderMessage(CultureInfo.InvariantCulture);
		var level = _sink.Events.Single().Level;
		Assert.Multiple(() =>
		{
			Assert.That(level, Is.EqualTo(LogEventLevel.Warning));
			Assert.That(message, Does.Contain("SomeOverlay"));
			Assert.That(message, Does.Contain("4242"));
			Assert.That(message, Does.Contain("OtherProgram"));
		});
	}

	[Test]
	public void The_same_denial_on_every_retry_warns_only_once()
	{
		var denial = TestDenials.Of(DiscordIpcDenialCause.Blocked);

		_log.Record(denial);
		_log.Record(denial);
		_log.Record(denial);

		Assert.That(_sink.Events.Select(e => e.Level),
			Is.EqualTo(new[] { LogEventLevel.Warning, LogEventLevel.Debug, LogEventLevel.Debug }));
	}

	[Test]
	public void A_changed_cause_warns_again()
	{
		_log.Record(TestDenials.Of(DiscordIpcDenialCause.Blocked));
		_log.Record(TestDenials.Of(DiscordIpcDenialCause.PrivilegeMismatch));

		Assert.That(_sink.Events.Select(e => e.Level),
			Is.EqualTo(new[] { LogEventLevel.Warning, LogEventLevel.Warning }));
	}

	[Test]
	public void A_denial_after_an_attempt_without_one_warns_again()
	{
		var denial = TestDenials.Of(DiscordIpcDenialCause.Blocked);

		_log.Record(denial);
		_log.Record(null);
		_log.Record(denial);

		Assert.That(_sink.Events.Select(e => e.Level),
			Is.EqualTo(new[] { LogEventLevel.Warning, LogEventLevel.Warning }));
	}

	private sealed class CapturingSink : ILogEventSink
	{
		private readonly List<LogEvent> _events = [];

		public IReadOnlyList<LogEvent> Events => _events;

		public void Emit(LogEvent logEvent)
		{
			_events.Add(logEvent);
		}
	}
}
