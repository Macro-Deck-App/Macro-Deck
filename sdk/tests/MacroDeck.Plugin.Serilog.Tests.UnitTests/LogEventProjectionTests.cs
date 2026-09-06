using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace MacroDeck.Plugin.Serilog.Tests.UnitTests;

/// <summary>
/// What a real Serilog pipeline hands <see cref="LogEventProjection" />, end to end from
/// <c>ILogger.Warning(...)</c> through <see cref="MacroDeckLogSink" /> to the wire DTO.
/// </summary>
[TestFixture]
public class LogEventProjectionTests
{
	[Test]
	public void A_logged_event_reaches_the_wire_with_every_supported_field()
	{
		var sink = new MacroDeckLogSink(Options.Create(new MacroDeckLoggingOptions()));

		// A fixed, non-UTC offset: the real ambient clock cannot be trusted to have a non-zero offset
		// (a CI runner commonly runs with TZ=UTC), and what this asserts is that the offset survives
		// the trip unchanged - not that it happens to be non-zero because of where the test runs.
		var fixedTimestamp = new DateTimeOffset(2026, 3, 4, 10, 30, 0, TimeSpan.FromHours(5));
		var logger = new LoggerConfiguration()
			.MinimumLevel.Verbose()
			.WriteTo.Sink(new RetimingSink(sink, fixedTimestamp))
			.CreateLogger();

		var inner = new InvalidOperationException("inner failure");
		var outer = new SceneSwitchException("outer failure", inner);

		logger.Warning(outer, "Scene {Scene} switched after {Ms} ms", "main", 42);

		var drained = sink.Drain(10);
		Assert.That(drained.Events, Has.Count.EqualTo(1));

		var dto = LogEventProjection.ToDto(drained.Events[0]);

		Assert.Multiple(() =>
		{
			Assert.That(dto.MessageTemplate, Does.Contain("{Scene}"));
			Assert.That(dto.RenderedMessage, Does.Contain("main").And.Contain("42").And.Contain("switched after"));
			Assert.That(dto.Properties, Is.Not.Null);
			Assert.That(dto.Properties!["Scene"], Is.EqualTo("main"));
			Assert.That(dto.Properties!["Ms"], Is.EqualTo("42"));
			Assert.That(dto.Timestamp.Offset, Is.EqualTo(TimeSpan.FromHours(5)));

			Assert.That(dto.Exception, Is.Not.Null);
			Assert.That(dto.Exception!.Type, Does.Contain(nameof(SceneSwitchException)));
			Assert.That(dto.Exception.Message, Does.Contain("outer failure"));
			Assert.That(dto.Exception.Inner, Is.Not.Null);
			Assert.That(dto.Exception.Inner!.Type, Does.Contain(nameof(InvalidOperationException)));
			Assert.That(dto.Exception.Inner.Message, Does.Contain("inner failure"));
		});
	}

	[Test]
	public void A_logged_event_carries_a_lossy_property_value_without_corrupting_the_template()
	{
		var sink = new MacroDeckLogSink(Options.Create(new MacroDeckLoggingOptions()));
		var logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();

		// The property value itself contains what looks like another template hole. A text-only relay
		// that re-parses the rendered message to recover properties would split this wrong; carrying
		// the template and the property as Serilog already separated them must not.
		logger.Information("Scene {Scene}", "main {Ms}");

		var drained = sink.Drain(10);
		var dto = LogEventProjection.ToDto(drained.Events[0]);

		Assert.Multiple(() =>
		{
			Assert.That(dto.MessageTemplate, Is.EqualTo("Scene {Scene}"));
			Assert.That(dto.Properties!["Scene"], Is.EqualTo("main {Ms}"));
		});
	}

	[Test]
	public void A_non_scalar_property_is_forwarded_as_a_rendered_scalar_and_never_drops_the_event()
	{
		var sink = new MacroDeckLogSink(Options.Create(new MacroDeckLoggingOptions()));
		var logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();

		Assert.DoesNotThrow(() =>
		{
			logger.Information("Config {@Config}", new { Host = "x", Port = 1 });
			logger.Information("Values {Values}", new[] { 1, 2, 3 });
		});

		var drained = sink.Drain(10);
		Assert.That(drained.Events, Has.Count.EqualTo(2));

		var dtos = drained.Events.Select(LogEventProjection.ToDto).ToList();

		Assert.Multiple(() =>
		{
			Assert.That(dtos[0].RenderedMessage, Is.Not.Empty);
			Assert.That(dtos[0].Properties, Is.Not.Null);
			Assert.That(dtos[0].Properties!.ContainsKey("Config"), Is.True);
			Assert.That(dtos[0].Properties!["Config"], Is.Not.Empty);

			Assert.That(dtos[1].RenderedMessage, Is.Not.Empty);
			Assert.That(dtos[1].Properties, Is.Not.Null);
			Assert.That(dtos[1].Properties!.ContainsKey("Values"), Is.True);
			Assert.That(dtos[1].Properties!["Values"], Is.Not.Empty);
		});
	}

	/// <summary>Forwards every event unchanged except for its timestamp, so a test can pin the offset
	/// deterministically while everything else still comes from a real Serilog pipeline.</summary>
	private sealed class RetimingSink(ILogEventSink inner, DateTimeOffset timestamp) : ILogEventSink
	{
		public void Emit(LogEvent logEvent)
			=> inner.Emit(new LogEvent(timestamp,
				logEvent.Level,
				logEvent.Exception,
				logEvent.MessageTemplate,
				logEvent.Properties.Select(property => new LogEventProperty(property.Key, property.Value))));
	}

	private sealed class SceneSwitchException : Exception
	{
		public SceneSwitchException(string message, Exception inner)
			: base(message, inner)
		{
		}
	}
}
