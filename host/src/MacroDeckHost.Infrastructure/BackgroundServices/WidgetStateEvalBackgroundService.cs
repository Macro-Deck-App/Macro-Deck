using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Widgets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public sealed class WidgetStateEvalBackgroundService : HostReadyBackgroundService
{
	private static readonly TimeSpan _debounce = TimeSpan.FromMilliseconds(200);
	private static readonly TimeSpan _pollTick = TimeSpan.FromSeconds(1);
	private static readonly TimeSpan _minPollInterval = TimeSpan.FromSeconds(1);
	private static readonly TimeSpan _maxPollInterval = TimeSpan.FromMinutes(2);

	// Nothing else would ever enqueue a provider-backed button - providers are not variable-driven -
	// so an idle one (nothing subscribed) is still polled, just far less often, to keep vars.state and
	// a later subscriber's first push reasonably fresh without hammering the provider for nothing.
	private static readonly TimeSpan _idlePollInterval = TimeSpan.FromSeconds(30);

	private readonly WidgetStateEvalChannel _queue;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly WidgetStateSubscriptionTracker _subscriptions;
	private readonly IFolderCache _folderCache;
	private readonly IWidgetVariableIndex _variableIndex;
	private readonly StartupReadiness _readiness;
	private readonly ILogger _logger;
	private readonly WidgetOptimisticStateStore? _optimisticStates;

	private readonly Dictionary<Guid, DateTimeOffset> _nextPollAt = new();

	public WidgetStateEvalBackgroundService(
		IHostApplicationLifetime lifetime,
		WidgetStateEvalChannel queue,
		IServiceScopeFactory scopeFactory,
		WidgetStateSubscriptionTracker subscriptions,
		IFolderCache folderCache,
		IWidgetVariableIndex variableIndex,
		StartupReadiness readiness,
		ILogger logger,
		WidgetOptimisticStateStore? optimisticStates = null)
		: base(lifetime)
	{
		_queue = queue;
		_scopeFactory = scopeFactory;
		_subscriptions = subscriptions;
		_folderCache = folderCache;
		_variableIndex = variableIndex;
		_readiness = readiness;
		_logger = logger;
		_optimisticStates = optimisticStates;
	}

	protected override async Task ExecuteWhenReady(CancellationToken stoppingToken)
	{
		await _readiness.WhenReady.WaitAsync(stoppingToken);

		_variableIndex.Rebuild();
		SeedExistingButtons();

		await Task.WhenAll(ConsumeQueue(stoppingToken), PollProviders(stoppingToken));
	}

	private async Task ConsumeQueue(CancellationToken stoppingToken)
	{
		var reader = _queue.Reader;
		while (await reader.WaitToReadAsync(stoppingToken))
		{
			var batch = new HashSet<Guid>();
			while (reader.TryRead(out var id))
			{
				batch.Add(id);
			}

			await Task.Delay(_debounce, stoppingToken);
			while (reader.TryRead(out var id))
			{
				batch.Add(id);
			}

			foreach (var widgetId in batch)
			{
				try
				{
					await Process(widgetId, stoppingToken);
				}
				catch (Exception ex)
				{
					_logger.Error(ex, "Failed to reconcile state for widget {WidgetId}", widgetId);
				}
			}
		}
	}

	private async Task PollProviders(CancellationToken stoppingToken)
	{
		using var timer = new PeriodicTimer(_pollTick);
		while (await timer.WaitForNextTickAsync(stoppingToken))
		{
			try
			{
				EnqueueDueProviders();
			}
			catch (Exception ex)
			{
				_logger.Error(ex, "Failed to schedule provider state polls");
			}
		}
	}

	private void EnqueueDueProviders()
	{
		foreach (var widgetId in _optimisticStates?.Expire() ?? [])
		{
			_queue.Enqueue(widgetId);
		}

		using var scope = _scopeFactory.CreateScope();
		var stateService = scope.ServiceProvider.GetRequiredService<IWidgetStateService>();
		var now = DateTimeOffset.UtcNow;
		var seen = new HashSet<Guid>();

		foreach (var widget in _folderCache.GetAllFolders().SelectMany(folder => folder.Widgets))
		{
			if (widget.Type != WidgetTypeIds.ActionButton)
			{
				continue;
			}

			seen.Add(widget.Id);

			// GetProviderPollInterval parses the whole data blob and its flows, which is far too much
			// to do for every action button on every tick. A button with no provider cannot mention
			// the key at all, so this text scan rules most of them out first; a false positive only
			// costs the parse that would have happened anyway. Deliberately the same trick
			// WidgetVariableReferenceParser uses, for the same reason.
			if (widget.Data is null || !widget.Data.Contains("stateProvider", StringComparison.Ordinal))
			{
				_nextPollAt.Remove(widget.Id);
				continue;
			}

			// Synchronous and side-effect free - reads the stored parameters and the action's declared
			// interval, never the provider itself.
			if (stateService.GetProviderPollInterval(widget.Id) is not { } declared)
			{
				_nextPollAt.Remove(widget.Id);
				continue;
			}

			if (_nextPollAt.TryGetValue(widget.Id, out var dueAt) && dueAt > now)
			{
				continue;
			}

			var hasSubscribers = _subscriptions.HasSubscribers(widget.Id.ToString());
			var interval = Clamp(declared);
			if (!hasSubscribers && interval < _idlePollInterval)
			{
				interval = _idlePollInterval;
			}

			_nextPollAt[widget.Id] = now + interval;
			_queue.Enqueue(widget.Id);
		}

		if (_nextPollAt.Count > 0)
		{
			foreach (var staleId in _nextPollAt.Keys.Where(id => !seen.Contains(id)).ToList())
			{
				_nextPollAt.Remove(staleId);
			}
		}
	}

	private static TimeSpan Clamp(TimeSpan requested)
		=> requested < _minPollInterval ? _minPollInterval :
			requested > _maxPollInterval ? _maxPollInterval : requested;

	private void SeedExistingButtons()
	{
		foreach (var widget in _folderCache.GetAllFolders().SelectMany(folder => folder.Widgets))
		{
			if (widget.Type == WidgetTypeIds.ActionButton)
			{
				_queue.Enqueue(widget.Id);
			}
		}
	}

	private async Task Process(Guid widgetId, CancellationToken cancellationToken)
	{
		using var scope = _scopeFactory.CreateScope();
		var reconciler = scope.ServiceProvider.GetRequiredService<IWidgetStateReconciler>();

		await reconciler.Reconcile(widgetId, cancellationToken);
	}
}
