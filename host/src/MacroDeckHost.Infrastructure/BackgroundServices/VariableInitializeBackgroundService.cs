using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.Variables.Files;
using MacroDeckHost.Domain.Entities;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public sealed class VariableInitializeBackgroundService : HostReadyBackgroundService
{
	private static readonly TimeSpan FileReadBudget = TimeSpan.FromSeconds(3);

	private readonly VariableRegistry _registry;
	private readonly FileVariableSynchronizer _files;
	private readonly IUserVariableStore _userStore;
	private readonly StartupReadiness _readiness;
	private readonly IHostApplicationLifetime _lifetime;
	private readonly ILogger _logger;

	public VariableInitializeBackgroundService(
		IHostApplicationLifetime lifetime,
		VariableRegistry registry,
		IUserVariableStore userStore,
		StartupReadiness readiness,
		ILogger logger,
		FileVariableSynchronizer files)
		: base(lifetime)
	{
		_registry = registry;
		_files = files;
		_userStore = userStore;
		_readiness = readiness;
		_lifetime = lifetime;
		_logger = logger;
	}

	protected override async Task ExecuteWhenReady(CancellationToken stoppingToken)
	{
		try
		{
			await LoadVariables();
		}
		catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
		{
			_logger.Fatal(ex, "Failed to load the user variables; stopping the host");
			_lifetime.StopApplication();
		}
	}

	// Files are read before MarkVariablesReady: applying them afterwards would fire a change event, and
	// with it every VariableChanged trigger, on each start.
	private async Task LoadVariables()
	{
		var stored = _userStore.Load();
		var fileVariables = stored.Where(variable => variable.FileSource is not null).ToList();
		var reads = fileVariables.ToDictionary(variable => variable.Id, variable => _files.AttachAsync(variable));
		await Task.WhenAny(Task.WhenAll(reads.Values), Task.Delay(FileReadBudget));

		foreach (var variable in stored)
		{
			if (variable.FileSource is null)
			{
				_registry.Upsert(variable);
				continue;
			}

			var read = reads[variable.Id];
			var initial = read.IsCompletedSuccessfully ? read.Result : null;
			variable.Value = initial?.Value ?? VariableValueSerializer.Serialize(variable.Type, null, variable.DecimalPlaces);
			_registry.Upsert(variable, initial?.Available ?? false);
		}

		foreach (var variable in fileVariables)
		{
			_files.Activate(variable.Id);
			if (!reads[variable.Id].IsCompletedSuccessfully)
			{
				_files.ScheduleRefresh(variable.Id);
			}
		}

		_readiness.MarkVariablesReady();
		_logger.Information("Loaded {Count} user variable(s) into the registry", stored.Count);
	}
}
