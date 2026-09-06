using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

/// <summary>
/// Restores every persisted variable-catalog binding into the registry at boot, unavailable until the
/// subscription coordinator's first reconcile proves otherwise. Runs after
/// <see cref="VariableInitializeBackgroundService"/> has loaded user variables - both gate on
/// <see cref="StartupReadiness.WhenReady"/>, and that service is what marks it ready - so a user variable
/// that already holds a binding's name wins the name and the binding is re-derived instead.
/// </summary>
public sealed class VariableBindingRestoreBackgroundService : HostReadyBackgroundService
{
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly IVariableBindingStore _bindingStore;
	private readonly VariableNameFactory _nameFactory;
	private readonly IVariableSubscriptionCoordinator _coordinator;
	private readonly StartupReadiness _readiness;
	private readonly ILogger _logger;

	public VariableBindingRestoreBackgroundService(
		IHostApplicationLifetime lifetime,
		IServiceScopeFactory scopeFactory,
		IVariableBindingStore bindingStore,
		VariableNameFactory nameFactory,
		IVariableSubscriptionCoordinator coordinator,
		StartupReadiness readiness,
		ILogger logger)
		: base(lifetime)
	{
		_scopeFactory = scopeFactory;
		_bindingStore = bindingStore;
		_nameFactory = nameFactory;
		_coordinator = coordinator;
		_readiness = readiness;
		_logger = logger;
	}

	protected override async Task ExecuteWhenReady(CancellationToken stoppingToken)
	{
		await _readiness.WhenReady.WaitAsync(stoppingToken);
		await RestoreBindingsAsync(stoppingToken);
	}

	/// <summary>The restore pass itself, factored out of <see cref="ExecuteWhenReady"/> so a test can drive
	/// it directly instead of racing <see cref="StartupReadiness"/>.</summary>
	internal async Task RestoreBindingsAsync(CancellationToken stoppingToken)
	{
		var bindings = _bindingStore.Load().ToList();
		if (bindings.Count == 0)
		{
			return;
		}

		var rewritten = false;

		await using var scope = _scopeFactory.CreateAsyncScope();
		var variableService = scope.ServiceProvider.GetRequiredService<IVariableService>();

		foreach (var binding in bindings)
		{
			var result = await variableService.MaterializeCatalogVariable(binding.IntegrationId,
				binding.LocalResourceId,
				binding.Name,
				binding.Type,
				binding.DecimalPlaces);

			if (!result.Success)
			{
				// The name is occupied by something else - a user variable in particular must win it.
				// Re-derive and materialize under the new name so the resource still gets one, and rewrite
				// the store so the new name survives the next restart too.
				var derived = _nameFactory.Derive(binding.IntegrationId, binding.LocalResourceId, binding.Name);
				binding.Name = derived;
				rewritten = true;

				result = await variableService.MaterializeCatalogVariable(binding.IntegrationId,
					binding.LocalResourceId,
					derived,
					binding.Type,
					binding.DecimalPlaces);
			}

			if (!result.Success || result.Data is null)
			{
				_logger.Error("Could not materialize variable binding '{Integration}'/'{Resource}': {Error}",
					binding.IntegrationId,
					binding.LocalResourceId,
					result.Error);
				continue;
			}

			// Unavailable until the coordinator's reconcile proves a provider is actually there - a
			// resource that disappeared must not read as available just because it was restored.
			await variableService.SetIntegrationVariableAvailability(binding.IntegrationId, result.Data.Id, false);
		}

		if (rewritten && !_bindingStore.Save(bindings))
		{
			_logger.Error("Could not persist variable bindings renamed during restore; " +
				"the renamed bindings will be re-derived again on the next start");
		}

		_logger.Information("Restored {Count} variable binding(s)", bindings.Count);

		await _coordinator.ReconcileAsync(stoppingToken);
	}
}
