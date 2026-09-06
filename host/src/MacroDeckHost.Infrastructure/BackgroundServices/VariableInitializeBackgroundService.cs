using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public sealed class VariableInitializeBackgroundService : HostReadyBackgroundService
{
	private readonly VariableRegistry _registry;
	private readonly IUserVariableStore _userStore;
	private readonly StartupReadiness _readiness;
	private readonly ILogger _logger;

	public VariableInitializeBackgroundService(
		IHostApplicationLifetime lifetime,
		VariableRegistry registry,
		IUserVariableStore userStore,
		StartupReadiness readiness,
		ILogger logger)
		: base(lifetime)
	{
		_registry = registry;
		_userStore = userStore;
		_readiness = readiness;
		_logger = logger;
	}

	protected override Task ExecuteWhenReady(CancellationToken stoppingToken)
	{
		var stored = _userStore.Load();
		foreach (var variable in stored)
		{
			_registry.Upsert(variable);
		}

		_readiness.MarkVariablesReady();
		_logger.Information("Loaded {Count} user variable(s) into the registry", stored.Count);
		return Task.CompletedTask;
	}
}
