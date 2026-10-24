using MacroDeckHost.Integrations.AutoHotkey.Protocol;
using Serilog;
using Serilog.Core;

namespace MacroDeckHost.Tests.UnitTests.AutoHotkey;

[TestFixture]
internal sealed class HelperProtocolTests
{
	private const char Rs = '\u001e';

	[Test]
	public async Task Plain_output_passes_through_and_only_the_message_part_of_a_line_is_capped()
	{
		var longOutput = new string('o', 50);
		var longMessage = Rs + "{\"t\":\"get\",\"id\":7,\"name\":\"" + new string('n', 100) + "\"}";
		var reader = new StringReader($"hello\r\n{longOutput}\nbefore{longMessage}\nlast");

		var lines = new List<HelperLine>();
		await foreach (var line in HelperLineReader.ReadAsync(reader, 32, CancellationToken.None))
		{
			lines.Add(line);
		}

		var output = string.Concat(lines.Where(line => !line.Text.Contains(Rs)).Select(line => line.Text));
		var message = lines.Single(line => line.Text.Contains(Rs));
		Assert.Multiple(() =>
		{
			Assert.That(lines[0], Is.EqualTo(new HelperLine("hello", true, false)));
			Assert.That(output, Does.Contain(longOutput));
			Assert.That(message.Truncated, Is.True);
			Assert.That(message.Text, Does.StartWith("before" + Rs));
			Assert.That(message.Text.Length, Is.LessThanOrEqualTo("before".Length + 34));
			Assert.That(lines[^1], Is.EqualTo(new HelperLine("last", true, false)));
		});
	}

	[Test]
	public void A_message_line_is_split_from_the_output_written_before_it()
	{
		var parser = new HelperMessageParser(new ManualClock());

		var result = parser.Parse(new HelperLine("partial" + Rs + "{\"t\":\"log\",\"text\":\"a\\nb\"}", true, false));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(HelperParseStatus.Message));
			Assert.That(result.Output, Is.EqualTo("partial"));
			Assert.That(result.Message?.Kind, Is.EqualTo(HelperMessageKind.Log));
			Assert.That(result.Message?.Text, Is.EqualTo("a\nb"));
		});
	}

	[TestCase("not json")]
	[TestCase("{\"t\":\"unknown\"}")]
	[TestCase("[1,2]")]
	[TestCase("{\"t\":\"set\"}")]
	public void A_malformed_message_is_ignored(string payload)
	{
		var parser = new HelperMessageParser(new ManualClock());

		Assert.That(parser.Parse(new HelperLine(Rs + payload, true, false)).Status,
			Is.EqualTo(HelperParseStatus.Malformed));
	}

	[Test]
	public void An_oversized_variable_request_is_still_answered_by_its_id()
	{
		var parser = new HelperMessageParser(new ManualClock());

		var result = parser.Parse(new HelperLine(Rs + "{\"t\":\"get\",\"id\":42,\"name\":\"xxxx", true, true));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(HelperParseStatus.RejectedRequest));
			Assert.That(result.RequestId, Is.EqualTo(42));
		});
	}

	[Test]
	public void A_flood_drops_messages_but_rejects_requests_until_the_next_second()
	{
		var clock = new ManualClock();
		var parser = new HelperMessageParser(clock);
		for (var index = 0; index < HelperMessageParser.MaxMessagesPerSecond; index++)
		{
			Assert.That(parser.Parse(Log("x")).Status, Is.EqualTo(HelperParseStatus.Message));
		}

		var dropped = parser.Parse(Log("over"));
		var rejected = parser.Parse(new HelperLine(Rs + "{\"t\":\"get\",\"id\":3,\"name\":\"v\"}", true, false));
		var result = parser.Parse(new HelperLine(Rs + "{\"t\":\"result\",\"text\":\"final\"}", true, false));
		clock.Advance(TimeSpan.FromSeconds(1));
		var accepted = parser.Parse(Log("later"));

		Assert.Multiple(() =>
		{
			Assert.That(dropped.Status, Is.EqualTo(HelperParseStatus.Dropped));
			Assert.That(rejected.Status, Is.EqualTo(HelperParseStatus.RejectedRequest));
			Assert.That(rejected.RequestId, Is.EqualTo(3));
			Assert.That(result.Status, Is.EqualTo(HelperParseStatus.Message));
			Assert.That(accepted.Status, Is.EqualTo(HelperParseStatus.Message));
		});
	}

	[Test]
	public async Task Every_variable_request_gets_an_answer_line()
	{
		var input = new RecordingWriter();
		var handler = new StubHandler { Answer = name => name == "known" ? new HelperAnswer(true, "a\\b\nc") : null };
		await using (var channel = new HelperChannel(input, handler, SilentLogger()))
		{
			channel.Accept(Parsed("{\"t\":\"get\",\"id\":1,\"name\":\"known\"}"));
			channel.Accept(Parsed("{\"t\":\"get\",\"id\":2,\"name\":\"throws\"}"));
			channel.Accept(new HelperParseResult(HelperParseStatus.RejectedRequest) { RequestId = 3 });
			await WaitUntilAsync(() => input.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length == 3);
		}

		var answers = input.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
			.Order(StringComparer.Ordinal)
			.ToList();
		Assert.That(answers,
			Is.EqualTo(new[] { "1\tok\ta\\\\b\\nc", "2\terror\tno answer", "3\terror\trejected" }));
	}

	[Test]
	public async Task Messages_reach_the_handler_in_the_order_the_script_sent_them()
	{
		var handler = new StubHandler();
		await using (var channel = new HelperChannel(new RecordingWriter(), handler, SilentLogger()))
		{
			channel.Accept(Parsed("{\"t\":\"ready\",\"v\":1}"));
			channel.Accept(Parsed("{\"t\":\"set\",\"name\":\"v\",\"value\":\"1\"}"));
			channel.Accept(Parsed("{\"t\":\"result\",\"text\":\"done\"}"));
			channel.Accept(Parsed("{\"t\":\"notify\",\"title\":\"T\",\"message\":\"M\",\"level\":\"warning\"}"));
			await WaitUntilAsync(() => handler.Calls.Count == 4);
		}

		Assert.That(handler.Calls, Is.EqualTo(new[] { "ready", "set v=1", "result done", "notify T/M/warning" }));
	}

	private static HelperLine Log(string text)
	{
		return new HelperLine(Rs + "{\"t\":\"log\",\"text\":\"" + text + "\"}", true, false);
	}

	private static HelperParseResult Parsed(string payload)
	{
		return new HelperMessageParser(TimeProvider.System).Parse(new HelperLine(Rs + payload, true, false));
	}

	private static ILogger SilentLogger()
	{
		return Logger.None;
	}

	private static async Task WaitUntilAsync(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (!condition() && DateTime.UtcNow < deadline)
		{
			await Task.Delay(10);
		}
	}

	private sealed class StubHandler : IHelperHandler
	{
		public Func<string, HelperAnswer?> Answer { get; init; } = _ => null;

		public List<string> Calls { get; } = [];

		public void OnReady()
		{
			Record("ready");
		}

		public Task SetVariableAsync(string name, string value, CancellationToken cancellationToken)
		{
			Record($"set {name}={value}");
			return Task.CompletedTask;
		}

		public Task<HelperAnswer> GetVariableAsync(string name, CancellationToken cancellationToken)
		{
			return Answer(name) is { } answer
				? Task.FromResult(answer)
				: Task.FromException<HelperAnswer>(new InvalidOperationException("boom"));
		}

		public void Notify(string title, string message, string level)
		{
			Record($"notify {title}/{message}/{level}");
		}

		public void SetResult(string text)
		{
			Record($"result {text}");
		}

		public void Log(string text)
		{
			Record($"log {text}");
		}

		public void ReportError(HelperMessage error)
		{
			Record($"error {error.Text}");
		}

		public string RejectedRequestText()
		{
			return "rejected";
		}

		public string NoAnswerText()
		{
			return "no answer";
		}

		private void Record(string call)
		{
			lock (Calls)
			{
				Calls.Add(call);
			}
		}
	}
}
