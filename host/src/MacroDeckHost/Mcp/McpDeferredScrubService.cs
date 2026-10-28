using MacroDeckHost.Application.Mcp;
using MacroDeckHost.Application.Services;

namespace MacroDeckHost.Mcp;

public sealed class McpDeferredScrubService : BackgroundService
{
	private readonly IServiceScopeFactory _scopes;
	private readonly StartupReadiness _readiness;
	private readonly Serilog.ILogger _logger;

	public McpDeferredScrubService(IServiceScopeFactory scopes, StartupReadiness readiness, Serilog.ILogger logger)
	{
		_scopes = scopes;
		_readiness = readiness;
		_logger = logger.ForContext<McpDeferredScrubService>();
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		try
		{
			await _readiness.WhenReady.WaitAsync(stoppingToken);
			await using var scope = _scopes.CreateAsyncScope();
			await scope.ServiceProvider.GetRequiredService<IMcpSecretLedger>().ScrubDeferred(stoppingToken);
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
		}
		catch (Exception exception)
		{
			_logger.Warning(exception,
				"Scrubbing secrets kept for MCP undo failed; they are retried at the next start");
		}
	}
}
