using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeck.Sdk.Identity;
using MacroDeck.Sdk.Variables;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

/// <summary>
/// Drains <see cref="VariableUpdateChannel"/> into the variable registry, and polls the bound catalog
/// resources of every provider that does not push (<see cref="IVariableProvider.SupportsPush"/> is
/// <c>false</c>) on each resource's own refresh interval. The eager half of a provider is polled by
/// <see cref="IntegrationVariablePollingBackgroundService"/> instead: a provider that pushes its catalog
/// still has its eager variables read on their own cadence.
/// </summary>
public sealed class VariableCatalogUpdateBackgroundService : HostReadyBackgroundService
{
	private static readonly TimeSpan _pollTickInterval = TimeSpan.FromMilliseconds(250);
	private static readonly TimeSpan _defaultRefresh = TimeSpan.FromSeconds(5);

	private readonly IServiceScopeFactory _scopeFactory;
	private readonly VariableUpdateChannel _channel;
	private readonly VariableRegistry _registry;
	private readonly IVariableBindingStore _bindingStore;
	private readonly VariableCatalogProviders _providers;
	private readonly IVariableRefreshSignal _refresh;
	private readonly ILogger _logger;

	private readonly Dictionary<(string IntegrationId, string LocalResourceId), PollState> _polls = new();

	public VariableCatalogUpdateBackgroundService(
		IHostApplicationLifetime lifetime,
		IServiceScopeFactory scopeFactory,
		VariableUpdateChannel channel,
		VariableRegistry registry,
		IVariableBindingStore bindingStore,
		VariableCatalogProviders providers,
		IVariableRefreshSignal refresh,
		ILogger logger)
		: base(lifetime)
	{
		_scopeFactory = scopeFactory;
		_channel = channel;
		_registry = registry;
		_bindingStore = bindingStore;
		_providers = providers;
		_refresh = refresh;
		_logger = logger;
	}

	protected override async Task ExecuteWhenReady(CancellationToken stoppingToken)
	{
		await Task.WhenAll(DrainLoopAsync(stoppingToken), PollLoopAsync(stoppingToken));
	}

	private async Task DrainLoopAsync(CancellationToken stoppingToken)
	{
		try
		{
			while (!stoppingToken.IsCancellationRequested)
			{
				await _channel.WaitToReadAsync(stoppingToken);
				await ApplyPendingAsync(stoppingToken);
			}
		}
		catch (OperationCanceledException)
		{
		}
	}

	private async Task PollLoopAsync(CancellationToken stoppingToken)
	{
		using var timer = new PeriodicTimer(_pollTickInterval);
		try
		{
			while (await timer.WaitForNextTickAsync(stoppingToken))
			{
				try
				{
					await PollDueAsync(stoppingToken);
				}
				catch (Exception ex)
				{
					_logger.Error(ex, "Variable catalog poll tick failed");
				}
			}
		}
		catch (OperationCanceledException)
		{
		}
	}

	/// <summary>Applies every update currently queued. Exposed internally so a test can drive it without a
	/// timer.</summary>
	internal async Task ApplyPendingAsync(CancellationToken cancellationToken)
	{
		foreach (var update in _channel.DrainAvailable())
		{
			await Apply(update, cancellationToken);
		}
	}

	private async Task Apply(VariableUpdateChannel.Update update, CancellationToken cancellationToken)
	{
		if (!QualifiedId.TryCreate(update.IntegrationId,
			update.LocalResourceId,
			LocalIdKind.Resource,
			out var qualified))
		{
			return;
		}

		var variable = _registry.FindByDefinition(qualified);
		if (variable is null)
		{
			return;
		}

		await using var scope = _scopeFactory.CreateAsyncScope();
		var service = scope.ServiceProvider.GetRequiredService<IVariableService>();

		if (update.Value is null)
		{
			if (variable.Type == Domain.Enums.VariableType.Text)
			{
				// A Text resource has a well-defined empty value the same way a user-set Text variable
				// does (VariableDtoMapper.ParseInputValue treats a null input the same way) - unlike
				// Numeric or Boolean, there is nothing to lose by preferring "empty" over "unavailable"
				// here, and unavailable now makes the variable unresolvable in conditions, which changes
				// condition outcomes a provider publishing a genuinely empty string never intended.
				await service.ReportIntegrationVariableValue(update.IntegrationId,
					variable.Id,
					string.Empty,
					bounds: null);
			}
			else
			{
				await service.SetIntegrationVariableAvailability(update.IntegrationId, variable.Id, false);
			}
		}
		else
		{
			await service.ReportIntegrationVariableValue(update.IntegrationId,
				variable.Id,
				update.Value,
				update.Bounds);
		}
	}

	/// <summary>One poll tick for every bound resource of a non-push provider that is currently due.
	/// Exposed internally so a test can drive it without a timer.</summary>
	internal async Task PollDueAsync(CancellationToken cancellationToken)
	{
		var bindings = _bindingStore.Load();
		var live = new HashSet<(string, string)>();

		foreach (var group in bindings.GroupBy(b => b.IntegrationId, StringComparer.Ordinal))
		{
			// Drained before the push check so a push provider's requests cannot accumulate: it publishes on
			// its own and has no poll to bring forward.
			var bringForward = ResourcesToRefresh(group.Key);

			var provider = _providers.Resolve(group.Key);
			if (provider is null || provider.SupportsPush)
			{
				continue;
			}

			foreach (var binding in group)
			{
				var key = (group.Key, binding.LocalResourceId);
				live.Add(key);

				if (!_polls.TryGetValue(key, out var state))
				{
					state = new PollState(await ResolveRefreshInterval(provider,
						binding.LocalResourceId,
						cancellationToken));
					_polls[key] = state;
				}

				if (bringForward.Contains(binding.LocalResourceId))
				{
					state.Schedule.MarkDueNow();
				}

				if (state.Schedule.IsDue(DateTime.UtcNow) && state.TryBeginRead())
				{
					_ = PollAndRelease(group.Key, provider, binding.LocalResourceId, state, cancellationToken);
				}
			}
		}

		foreach (var stale in _polls.Keys.Where(k => !live.Contains(k)).ToList())
		{
			_polls.Remove(stale);
		}
	}

	// A write is applied by the owner and never echoed, so the value the host shows still comes from a
	// read; this is what pulls that read forward instead of leaving the control on the pre-write reading.
	private HashSet<string> ResourcesToRefresh(string integrationId)
	{
		var pending = _refresh.DrainFor(integrationId);
		if (pending.Count == 0)
		{
			return [];
		}

		var resources = new HashSet<string>(StringComparer.Ordinal);
		foreach (var variableId in pending)
		{
			var definitionId = _registry.GetById(variableId)?.DefinitionId;
			if (definitionId is not null)
			{
				resources.Add(definitionId);
			}
		}

		return resources;
	}

	private async Task<TimeSpan> ResolveRefreshInterval(
		IVariableProvider provider,
		string localResourceId,
		CancellationToken cancellationToken)
	{
		try
		{
			var definition = await provider.ResolveAsync(localResourceId, cancellationToken);
			return definition?.RefreshInterval ?? _defaultRefresh;
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Could not resolve refresh interval for '{Id}'", localResourceId);
			return _defaultRefresh;
		}
	}

	private async Task PollAndRelease(
		string integrationId,
		IVariableProvider provider,
		string localResourceId,
		PollState state,
		CancellationToken cancellationToken)
	{
		try
		{
			VariableReading reading;
			try
			{
				reading = await provider.ReadAsync(localResourceId, cancellationToken);
			}
			catch (Exception ex)
			{
				_logger.Error(ex,
					"Variable catalog provider '{Integration}' failed to poll '{Id}'",
					integrationId,
					localResourceId);
				reading = VariableReading.Unavailable;
			}

			// An unavailable reading carries no bounds, and passing none leaves whatever range the variable
			// already has in place rather than collapsing it for the length of a transient outage.
			var bounds = reading.Value is null
				? null
				: new VariableBounds(reading.Min, reading.Max, reading.Step);

			_channel.Write(integrationId, localResourceId, reading.Value, bounds);
		}
		finally
		{
			state.EndRead();
		}
	}

	private sealed class PollState
	{
		private int _reading;

		public PollState(TimeSpan interval)
		{
			Schedule = new VariablePollSchedule(interval);
		}

		public VariablePollSchedule Schedule { get; }

		public bool TryBeginRead() => Interlocked.CompareExchange(ref _reading, 1, 0) == 0;

		public void EndRead() => Interlocked.Exchange(ref _reading, 0);
	}
}
