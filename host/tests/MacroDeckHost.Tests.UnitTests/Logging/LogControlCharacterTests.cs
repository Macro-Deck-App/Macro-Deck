using System.Globalization;
using MacroDeckHost.Logging;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;

namespace MacroDeckHost.Tests.UnitTests.Logging;

[TestFixture]
public class LogControlCharacterTests
{
	private static readonly char Escape = (char)0x1B;

	[Test]
	public void An_untrusted_property_value_cannot_forge_a_line_or_carry_a_terminal_escape()
	{
		var rendered = RenderThroughSink(logger => logger.Warning("Report from {UserAgent}",
			$"ua\n2026-09-30 00:00:00.000 +00:00 [ERR] [Host] forged{Escape}[31m"));

		AssertClean(rendered);
	}

	[Test]
	public void Untrusted_text_inside_the_message_template_is_neutralised_too()
	{
		var template = new MessageTemplate([new TextToken($"plugin says {{not a hole}}\n[ERR] [Host] forged{Escape}")]);
		var logEvent = new LogEvent(DateTimeOffset.UtcNow, LogEventLevel.Information, null, template, []);

		var rendered = Render(sink => new RedactingSink(sink).Emit(logEvent));

		Assert.Multiple(() =>
		{
			AssertClean(rendered);
			Assert.That(rendered, Does.Contain("{not a hole}"));
		});
	}

	[Test]
	public void Values_nested_in_collections_and_dictionary_keys_are_neutralised()
	{
		var data = new Dictionary<string, string[]> { [$"key\n[ERR]{Escape}"] = ["a\nb", $"c{Escape}"] };

		var rendered = RenderThroughSink(logger => logger.Information("Data {Data}", data));

		AssertClean(rendered);
	}

	[Test]
	public void Dictionary_keys_that_neutralise_to_the_same_text_do_not_drop_the_event()
	{
		var data = new Dictionary<string, int> { ["a\nb"] = 1, ["a\rb"] = 2, ["a\\nb"] = 3 };

		var rendered = RenderThroughSink(logger => logger.Information("Data {Data}", data));

		Assert.Multiple(() =>
		{
			AssertClean(rendered);
			Assert.That(rendered, Does.Contain("1").And.Contain("2").And.Contain("3"));
		});
	}

	[Test]
	public void Char_and_uri_values_are_neutralised_and_unaffected_ones_keep_their_reference()
	{
		var forged = new Uri($"http://example.test/a{Escape}b");
		var clean = new Uri("http://example.test/ok");
		Assume.That(forged.ToString().Contains(Escape), Is.True);
		var logEvent = new LogEvent(DateTimeOffset.UtcNow,
			LogEventLevel.Information,
			null,
			new MessageTemplateParser().Parse("{Forged} {Clean} {Newline} {Letter}"),
			[
				new LogEventProperty("Forged", new ScalarValue(forged)),
				new LogEventProperty("Clean", new ScalarValue(clean)),
				new LogEventProperty("Newline", new ScalarValue('\n')),
				new LogEventProperty("Letter", new ScalarValue('x'))
			]);

		var redacted = RedactingSink.Redact(logEvent);

		Assert.Multiple(() =>
		{
			AssertClean(redacted.RenderMessage(CultureInfo.InvariantCulture));
			Assert.That(redacted.Properties["Clean"], Is.SameAs(logEvent.Properties["Clean"]));
			Assert.That(redacted.Properties["Letter"], Is.SameAs(logEvent.Properties["Letter"]));
		});
	}

	[Test]
	public void A_secret_split_over_a_line_break_is_still_redacted()
	{
		var rendered = RenderThroughSink(logger => logger.Warning("Failed: {Command}", "token:\nabcdefghijkl"));

		Assert.Multiple(() =>
		{
			AssertClean(rendered);
			Assert.That(rendered, Does.Not.Contain("abcdefghijkl"));
		});
	}

	[Test]
	public void An_exception_loses_terminal_escapes_but_keeps_its_line_breaks()
	{
		var exception = new InvalidOperationException($"bad{Escape}[31m\nsecond line");
		var events = new List<LogEvent>();

		using (var logger = new LoggerConfiguration()
			.WriteTo.Redacted(sinks => sinks.Sink(new DelegatingLogSink(events.Add)))
			.CreateLogger())
		{
			logger.Error(exception, "Failed");
		}

		var text = events.Single().Exception!.ToString();
		Assert.Multiple(() =>
		{
			Assert.That(text.Contains(Escape), Is.False);
			Assert.That(text, Does.Contain("\nsecond line"));
		});
	}

	private static string RenderThroughSink(Action<ILogger> log)
		=> Render(sink =>
		{
			using var logger = new LoggerConfiguration()
				.MinimumLevel.Verbose()
				.WriteTo.Redacted(sinks => sinks.Sink(sink))
				.CreateLogger();
			log(logger);
		});

	private static string Render(Action<ILogEventSink> emit)
	{
		var events = new List<LogEvent>();
		emit(new DelegatingLogSink(events.Add));

		return events.Single().RenderMessage(CultureInfo.InvariantCulture);
	}

	private static void AssertClean(string rendered)
		=> Assert.That(rendered.Any(c => char.IsControl(c) && c != '\t'), Is.False, rendered);
}
