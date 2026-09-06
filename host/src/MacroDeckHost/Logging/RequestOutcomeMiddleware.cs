using System.Diagnostics;
using Serilog.Events;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Logging;

public sealed class RequestOutcomeMiddleware
{
	public const string Category = "MacroDeckHost.Http.Requests";

	private readonly RequestDelegate _next;
	private readonly ILogger _logger;

	public RequestOutcomeMiddleware(RequestDelegate next, ILogger logger)
	{
		ArgumentNullException.ThrowIfNull(logger);

		_next = next;
		_logger = logger.ForContext("SourceContext", Category);
	}

	public async Task Invoke(HttpContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		var started = Stopwatch.GetTimestamp();

		try
		{
			await _next(context).ConfigureAwait(false);
		}
		catch
		{
			// The exception handler sits outside this middleware, so it only turns the throw into the
			// response the client receives after this frame has unwound: the outcome has to be written
			// here, against the status that handler will send.
			Log(context, started, StatusCodes.Status500InternalServerError);

			throw;
		}

		Log(context, started, context.Response.StatusCode);
	}

	private void Log(HttpContext context, long started, int status)
	{
		var level = LevelFor(status);
		if (!_logger.IsEnabled(level))
		{
			return;
		}

		_logger.Write(level,
			"HTTP {Method} {Path} responded {Status} in {ElapsedMs} ms",
			context.Request.Method,
			context.Request.Path.Value,
			status,
			(long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
	}

	private static LogEventLevel LevelFor(int status)
		=> status switch
		{
			>= 500 => LogEventLevel.Warning,
			>= 400 => LogEventLevel.Information,
			_ => LogEventLevel.Debug
		};
}

public static class RequestOutcomeApplicationBuilderExtensions
{
	public static IApplicationBuilder UseRequestOutcomeLogging(this IApplicationBuilder app)
	{
		ArgumentNullException.ThrowIfNull(app);

		return app.UseMiddleware<RequestOutcomeMiddleware>();
	}
}
