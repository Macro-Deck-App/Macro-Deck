using System.Globalization;
using MacroDeckHost.Logging;
using Microsoft.AspNetCore.Http;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace MacroDeckHost.Tests.UnitTests.Logging;

public class RequestOutcomeMiddlewareTests
{
	[TestCase(404, LogEventLevel.Information)]
	[TestCase(409, LogEventLevel.Information)]
	[TestCase(500, LogEventLevel.Warning)]
	public async Task A_Request_That_Fails_Without_Throwing_Is_Still_Logged(int status, LogEventLevel expected)
	{
		var events = await Run(status, LogEventLevel.Information);

		Assert.That(events, Has.Count.EqualTo(1));
		Assert.Multiple(() =>
		{
			Assert.That(events[0].Level, Is.EqualTo(expected));
			Assert.That(events[0].RenderMessage(CultureInfo.InvariantCulture),
				Does.Contain(status.ToString(CultureInfo.InvariantCulture)).And.Contain("/api/thing"));
		});
	}

	[Test]
	public async Task A_Successful_Request_Says_Nothing_Above_Debug()
	{
		var atInformation = await Run(200, LogEventLevel.Information);
		var atDebug = await Run(200, LogEventLevel.Debug);

		Assert.Multiple(() =>
		{
			Assert.That(atInformation, Is.Empty);
			Assert.That(atDebug.Select(logEvent => logEvent.Level), Is.EqualTo(new[] { LogEventLevel.Debug }));
		});
	}

	[Test]
	public void A_Request_Whose_Pipeline_Throws_Is_Still_Logged()
	{
		var sink = new CollectingSink();
		using var logger = new LoggerConfiguration()
			.MinimumLevel.Is(LogEventLevel.Information)
			.WriteTo.Sink(sink)
			.CreateLogger();
		var middleware = new RequestOutcomeMiddleware(_ => throw new InvalidOperationException("boom"), logger);

		var httpContext = new DefaultHttpContext();
		httpContext.Request.Method = "GET";
		httpContext.Request.Path = "/api/thing";

		Assert.ThrowsAsync<InvalidOperationException>(() => middleware.Invoke(httpContext));

		Assert.That(sink.Events, Has.Count.EqualTo(1));
		Assert.Multiple(() =>
		{
			Assert.That(sink.Events[0].Level, Is.EqualTo(LogEventLevel.Warning));
			Assert.That(sink.Events[0].RenderMessage(CultureInfo.InvariantCulture),
				Does.Contain("500").And.Contain("/api/thing"));
		});
	}

	private static async Task<List<LogEvent>> Run(int status, LogEventLevel minimum)
	{
		var sink = new CollectingSink();
		using var logger = new LoggerConfiguration().MinimumLevel.Is(minimum).WriteTo.Sink(sink).CreateLogger();
		var middleware = new RequestOutcomeMiddleware(context =>
			{
				context.Response.StatusCode = status;

				return Task.CompletedTask;
			},
			logger);

		var httpContext = new DefaultHttpContext();
		httpContext.Request.Method = "GET";
		httpContext.Request.Path = "/api/thing";

		await middleware.Invoke(httpContext);

		return sink.Events;
	}

	private sealed class CollectingSink : ILogEventSink
	{
		public List<LogEvent> Events { get; } = [];

		public void Emit(LogEvent logEvent) => Events.Add(logEvent);
	}
}
