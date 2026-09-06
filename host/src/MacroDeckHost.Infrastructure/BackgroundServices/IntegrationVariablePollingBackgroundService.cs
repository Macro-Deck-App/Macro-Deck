using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeck.Sdk.Variables;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public sealed class IntegrationVariablePollingBackgroundService : HostReadyBackgroundService
{
	private static readonly TimeSpan _tickInterval = TimeSpan.FromMilliseconds(250);
	private static readonly TimeSpan _defaultRefresh = TimeSpan.FromSeconds(5);

	private readonly IIntegrationRegistry _integrations;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly IVariablePollingInvalidationSignal _invalidation;
	private readonly IVariableRefreshSignal _refresh;
	private readonly ILogger _logger;

	private readonly Dictionary<string, ProviderRuntime> _runtimes = new(StringComparer.Ordinal);

	public IntegrationVariablePollingBackgroundService(
		IHostApplicationLifetime lifetime,
		IIntegrationRegistry integrations,
		IServiceScopeFactory scopeFactory,
		IVariablePollingInvalidationSignal invalidation,
		IVariableRefreshSignal refresh,
		ILogger logger)
		: base(lifetime)
	{
		_integrations = integrations;
		_scopeFactory = scopeFactory;
		_invalidation = invalidation;
		_refresh = refresh;
		_logger = logger;
	}

	protected override async Task ExecuteWhenReady(CancellationToken stoppingToken)
	{
		using var timer = new PeriodicTimer(_tickInterval);
		try
		{
			while (await timer.WaitForNextTickAsync(stoppingToken))
			{
				try
				{
					await DispatchDue(stoppingToken);
				}
				catch (Exception ex)
				{
					_logger.Error(ex, "Integration variable poll tick failed");
				}
			}
		}
		catch (OperationCanceledException)
		{
		}
	}

	internal async Task DispatchDue(CancellationToken ct)
	{
		foreach (var staleId in _invalidation.DrainStale())
		{
			_runtimes.Remove(staleId);
		}

		foreach (var integration in _integrations.Integrations)
		{
			if (integration is not IVariableProvider provider ||
				!_integrations.IsEnabled(integration.Id) ||
				!integration.IsInitialized)
			{
				continue;
			}

			if (!_runtimes.TryGetValue(integration.Id, out var runtime))
			{
				runtime = await Register(integration.Id, provider, ct);
				_runtimes[integration.Id] = runtime;
			}

			runtime.BringForward(_refresh.DrainFor(integration.Id));

			var now = DateTime.UtcNow;
			foreach (var state in runtime.Variables.Values)
			{
				if (!state.TryBeginRead())
				{
					continue;
				}

				if (!state.Schedule.IsDue(now))
				{
					state.EndRead();
					continue;
				}

				_ = Task.Run(() => PollAndRelease(integration.Id, provider, state, ct), ct);
			}
		}
	}

	private async Task PollAndRelease(
		string integrationId,
		IVariableProvider provider,
		VariableState state,
		CancellationToken ct)
	{
		try
		{
			await Poll(integrationId, provider, state, ct);
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Polling variable '{Variable}' of '{Integration}' failed", state.Name, integrationId);
		}
		finally
		{
			state.EndRead();
		}
	}

	private async Task<ProviderRuntime> Register(string integrationId, IVariableProvider provider, CancellationToken ct)
	{
		var runtime = new ProviderRuntime();
		var registered = new Dictionary<Guid, string>();
		await using var scope = _scopeFactory.CreateAsyncScope();
		var service = scope.ServiceProvider.GetRequiredService<IVariableService>();

		foreach (var declared in Bounded(integrationId, provider.Variables))
		{
			if (declared.Materialization != VariableMaterialization.Eager)
			{
				_logger.Error("Integration '{Integration}' declares '{Name}' as an eager variable but marks it " +
					"{Materialization}; skipping it. A catalog resource is reached through the variable catalog.",
					integrationId,
					declared.Name,
					declared.Materialization);
				continue;
			}

			var definitionId = declared.ResolvedId;
			if (definitionId is null)
			{
				_logger.Error("Integration '{Integration}' declares an eager variable without a usable id; " +
					"skipping it. A template name is only legal in DeclaredVariables.",
					integrationId);
				continue;
			}

			var name = declared.Name ?? definitionId;
			var result = await service.CreateIntegrationVariable(integrationId,
				name,
				VariableScope.Global,
				null,
				SdkVariableTypeMapper.ToDomain(declared.Type),
				null,
				declared.DecimalPlaces,
				definitionId,
				VariableDeclarationFactory.From(declared));

			if (result is { Success: true, Data: not null })
			{
				if (registered.TryGetValue(result.Data.Id, out var claimedBy))
				{
					_logger.Error("Integration '{Integration}' declares '{Name}' and '{Other}' as the same variable; " +
						"skipping '{Name}'. Variable definition ids must be unique within an integration.",
						integrationId,
						name,
						claimedBy,
						name);
					continue;
				}

				registered[result.Data.Id] = name;
				runtime.Variables[definitionId] = new VariableState
				{
					Id = result.Data.Id,
					Name = name,
					DefinitionId = definitionId,
					Schedule = new VariablePollSchedule(declared.RefreshInterval ?? _defaultRefresh)
				};
			}
			else
			{
				_logger.Warning("Could not register provided variable '{Name}' for '{Integration}': {Error}",
					name,
					integrationId,
					result.Error);
			}
		}

		_logger.Information("Registered {Count} provided variable(s) for integration '{Integration}'",
			runtime.Variables.Count,
			integrationId);
		return runtime;
	}

	// An eager variable is polled for as long as the integration runs, so the eager set is the one that has
	// to stay bounded. The surplus is dropped rather than the provider refused: one tail-end mistake must
	// not take an integration's whole variable surface offline.
	private IReadOnlyList<VariableDefinition> Bounded(string integrationId, IReadOnlyList<VariableDefinition> declared)
	{
		if (declared.Count <= VariableLimits.MaxEagerVariablesPerProvider)
		{
			return declared;
		}

		_logger.Error("Integration '{Integration}' declares {Count} eager variables; keeping the first {Limit} in " +
			"declaration order and skipping the rest. A provider with more to offer exposes them through its " +
			"variable catalog.",
			integrationId,
			declared.Count,
			VariableLimits.MaxEagerVariablesPerProvider);

		return declared.Take(VariableLimits.MaxEagerVariablesPerProvider).ToList();
	}

	private async Task Poll(string integrationId, IVariableProvider provider, VariableState state, CancellationToken ct)
	{
		VariableReading reading;
		try
		{
			reading = await provider.ReadAsync(state.DefinitionId, ct);
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Provider '{Integration}' threw while reading a variable", integrationId);
			reading = VariableReading.Unavailable;
		}

		await using var scope = _scopeFactory.CreateAsyncScope();
		var service = scope.ServiceProvider.GetRequiredService<IVariableService>();

		if (reading.Value is null)
		{
			// Deliberately no bounds: an unavailable reading carries none, and clearing the range a control
			// is already drawn against would collapse it for the length of a transient outage.
			await service.SetIntegrationVariableAvailability(integrationId, state.Id, false);
		}
		else
		{
			await service.ReportIntegrationVariableValue(integrationId,
				state.Id,
				reading.Value,
				new VariableBounds(reading.Min, reading.Max, reading.Step));
		}
	}

	private sealed class ProviderRuntime
	{
		public Dictionary<string, VariableState> Variables { get; } = new(StringComparer.Ordinal);

		public void BringForward(IReadOnlyList<Guid> variableIds)
		{
			if (variableIds.Count == 0)
			{
				return;
			}

			foreach (var state in Variables.Values)
			{
				if (variableIds.Contains(state.Id))
				{
					state.Schedule.MarkDueNow();
				}
			}
		}
	}

	private sealed class VariableState
	{
		private int _reading;

		public required Guid Id { get; init; }
		public required string Name { get; init; }
		public required string DefinitionId { get; init; }
		public required VariablePollSchedule Schedule { get; init; }

		public bool TryBeginRead() => Interlocked.CompareExchange(ref _reading, 1, 0) == 0;

		public void EndRead() => Interlocked.Exchange(ref _reading, 0);
	}
}
