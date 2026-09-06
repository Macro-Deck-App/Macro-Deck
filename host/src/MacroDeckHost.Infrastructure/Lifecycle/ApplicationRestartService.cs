using MacroDeckHost.Application.Lifecycle;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Autostart;
using Microsoft.Extensions.Hosting;
using Serilog;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Lifecycle;

public class ApplicationRestartService : IApplicationRestartService
{
	private static readonly TimeSpan _defaultShutdownDelay = TimeSpan.FromMilliseconds(250);

	private readonly ILogger _logger = Log.ForContext<ApplicationRestartService>();
	private readonly IHostApplicationLifetime _lifetime;
	private readonly string? _shellExecutable;
	private readonly TimeSpan _shutdownDelay;
	private int _restartRequested;

	public ApplicationRestartService(IHostApplicationLifetime lifetime)
		: this(lifetime, AutostartService.ResolveShellExecutable(), _defaultShutdownDelay)
	{
	}

	public ApplicationRestartService(IHostApplicationLifetime lifetime,
		string? shellExecutable,
		TimeSpan shutdownDelay)
	{
		_lifetime = lifetime;
		_shellExecutable = shellExecutable;
		_shutdownDelay = shutdownDelay;
	}

	public RestartAvailability Availability => _shellExecutable is null
		? new RestartAvailability(false, "Restarting is only available in the installed desktop app")
		: new RestartAvailability(true, null);

	public bool RestartRequested => Volatile.Read(ref _restartRequested) == 1;

	public Result<RestartError> Request(string reason)
	{
		var availability = Availability;
		if (!availability.Supported)
		{
			return Result.Fail(RestartError.NotSupported, availability.Reason);
		}

		if (Interlocked.Exchange(ref _restartRequested, 1) == 1)
		{
			return Result.Ok<RestartError>();
		}

		_logger.Information("Restart requested, reason: {Reason}", reason);

		_ = Task.Run(async () =>
		{
			await Task.Delay(_shutdownDelay);
			_lifetime.StopApplication();
		});

		return Result.Ok<RestartError>();
	}
}
