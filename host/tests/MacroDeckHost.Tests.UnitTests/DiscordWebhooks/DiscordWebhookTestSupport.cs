using System.Globalization;
using System.Net;
using System.Text;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;
using Serilog.Events;

namespace MacroDeckHost.Tests.UnitTests.DiscordWebhooks;

internal static class DiscordWebhookTestSupport
{
	public const string Token = "aB3_dE-fGhIjKlMnOpQrStUvWxYz0123456789abcdefGHIJKL";

	public const string Url = "https://discord.com/api/webhooks/123456789012345678/" + Token;

	public static ILogger CapturingLogger(List<LogEvent> events)
	{
		return new LoggerConfiguration()
			.MinimumLevel.Verbose()
			.WriteTo.Sink(new DelegatingLogSink(events.Add))
			.CreateLogger();
	}

	public static string Rendered(IEnumerable<LogEvent> events)
	{
		var builder = new StringBuilder();
		foreach (var logEvent in events)
		{
			builder.AppendLine(logEvent.RenderMessage(CultureInfo.InvariantCulture));
			builder.AppendLine(logEvent.Exception?.ToString());
			foreach (var (name, value) in logEvent.Properties)
			{
				builder.AppendLine(CultureInfo.InvariantCulture, $"{name}={value}");
			}
		}

		return builder.ToString();
	}
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
	public override DateTimeOffset GetUtcNow()
	{
		return now;
	}
}

internal sealed class ScriptedDiscordHandler : HttpMessageHandler
{
	private readonly Queue<Func<HttpResponseMessage>> _responses = new();

	public List<(Uri Uri, string Body)> Requests { get; } = [];

	public ScriptedDiscordHandler Then(
		HttpStatusCode status,
		string body = "",
		Action<HttpResponseMessage>? configure = null)
	{
		_responses.Enqueue(() =>
		{
			var response = new HttpResponseMessage(status)
			{
				Content = new StringContent(body, Encoding.UTF8, "application/json")
			};
			configure?.Invoke(response);
			return response;
		});
		return this;
	}

	protected override async Task<HttpResponseMessage> SendAsync(
		HttpRequestMessage request,
		CancellationToken cancellationToken)
	{
		var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
		Requests.Add((request.RequestUri!, body));

		return _responses.Count > 0 ? _responses.Dequeue()() : new HttpResponseMessage(HttpStatusCode.NoContent);
	}
}
