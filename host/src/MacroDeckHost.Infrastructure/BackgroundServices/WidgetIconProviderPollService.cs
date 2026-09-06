using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Widgets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

/// <summary>
/// Polls every Action Button's icon-provider assignment and pushes a change to any open session, mirroring
/// <see cref="WidgetStateEvalBackgroundService" /> but kept entirely separate from it: an icon provider has
/// no optimistic-state machinery, and folding it into that file - already the most delicate code in the
/// state pipeline - would only put the two at risk of drifting into each other. An icon has a meaningful
/// default (fall back to the configured appearance icon), so unlike state there is nothing here to hold
/// onto across an unavailable poll.
/// </summary>
public sealed class WidgetIconProviderPollService : HostReadyBackgroundService
{
	private static readonly TimeSpan _debounce = TimeSpan.FromMilliseconds(200);
	private static readonly TimeSpan _pollTick = TimeSpan.FromSeconds(1);

	/// <summary>The published floor a declared <c>IconPollInterval</c> is clamped to - exposed so a test
	/// can assert the clamp against this value rather than a second, hard-coded literal that could drift
	/// from it unnoticed.</summary>
	internal static readonly TimeSpan MinPollInterval = TimeSpan.FromSeconds(1);

	/// <summary>The published ceiling a declared <c>IconPollInterval</c> is clamped to.</summary>
	internal static readonly TimeSpan MaxPollInterval = TimeSpan.FromMinutes(2);

	// Mirrors WidgetStateEvalBackgroundService's own idle floor: nothing subscribed still gets polled, far
	// less often, so a later subscriber's first render is reasonably fresh rather than starting blank.
	internal static readonly TimeSpan IdlePollInterval = TimeSpan.FromSeconds(30);

	private readonly WidgetIconEvalChannel _queue;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly IWidgetRenderSignals _renderSignals;
	private readonly WidgetStateSubscriptionTracker _subscriptions;
	private readonly IFolderCache _folderCache;
	private readonly StartupReadiness _readiness;
	private readonly IDeviceSurfaceRenderSink? _deviceSurfaces;
	private readonly ILogger _logger;

	private readonly Dictionary<Guid, DateTimeOffset> _nextPollAt = new();
	private readonly Dictionary<Guid, WidgetIconResolution> _lastResolution = new();

	public WidgetIconProviderPollService(
		IHostApplicationLifetime lifetime,
		WidgetIconEvalChannel queue,
		IServiceScopeFactory scopeFactory,
		IWidgetRenderSignals renderSignals,
		WidgetStateSubscriptionTracker subscriptions,
		IFolderCache folderCache,
		StartupReadiness readiness,
		ILogger logger,
		IDeviceSurfaceRenderSink? deviceSurfaces = null)
		: base(lifetime)
	{
		_queue = queue;
		_scopeFactory = scopeFactory;
		_renderSignals = renderSignals;
		_subscriptions = subscriptions;
		_folderCache = folderCache;
		_readiness = readiness;
		_deviceSurfaces = deviceSurfaces;
		_logger = logger.ForContext<WidgetIconProviderPollService>();
	}

	protected override async Task ExecuteWhenReady(CancellationToken stoppingToken)
	{
		await _readiness.WhenReady.WaitAsync(stoppingToken);

		SeedExistingButtons();

		await Task.WhenAll(ConsumeQueue(stoppingToken), PollProviders(stoppingToken));
	}

	private async Task ConsumeQueue(CancellationToken stoppingToken)
	{
		var reader = _queue.Reader;
		while (await reader.WaitToReadAsync(stoppingToken))
		{
			await Task.Delay(_debounce, stoppingToken);
			await ProcessQueuedAsync(stoppingToken);
		}
	}

	/// <summary>Drains and resolves whatever is currently queued, in one pass - exposed so a test can
	/// settle deterministically without waiting on the debounce or the tick timer.</summary>
	internal async Task ProcessQueuedAsync(CancellationToken cancellationToken)
	{
		var batch = new HashSet<Guid>();
		while (_queue.Reader.TryRead(out var id))
		{
			batch.Add(id);
		}

		foreach (var widgetId in batch)
		{
			try
			{
				await Process(widgetId, cancellationToken).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				_logger.Error(ex, "Failed to reconcile icon for widget {WidgetId}", widgetId);
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
				_logger.Error(ex, "Failed to schedule icon provider polls");
			}
		}
	}

	/// <summary>The scheduling decision alone - which widgets are due and get enqueued this tick - exposed
	/// so a test can drive it without a real timer.</summary>
	internal void EnqueueDueProviders()
	{
		using var scope = _scopeFactory.CreateScope();
		var iconService = scope.ServiceProvider.GetRequiredService<IWidgetIconService>();
		var now = DateTimeOffset.UtcNow;
		var seen = new HashSet<Guid>();

		foreach (var widget in _folderCache.GetAllFolders().SelectMany(folder => folder.Widgets))
		{
			if (widget.Type != WidgetTypeIds.ActionButton)
			{
				continue;
			}

			seen.Add(widget.Id);

			// A cheap text prefilter before ever parsing the flows/model, exactly like
			// WidgetVariableReferenceParser and WidgetStateEvalBackgroundService's own equivalent: a
			// button with no iconProvider at all cannot mention the key, so most buttons are ruled out
			// before the parse a false positive would have cost anyway.
			if (widget.Data is null || !widget.Data.Contains("iconProvider", StringComparison.Ordinal))
			{
				_nextPollAt.Remove(widget.Id);
				continue;
			}

			if (iconService.GetProviderPollInterval(widget.Id) is not { } declared)
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
			if (!hasSubscribers && interval < IdlePollInterval)
			{
				interval = IdlePollInterval;
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
		=> requested < MinPollInterval ? MinPollInterval :
			requested > MaxPollInterval ? MaxPollInterval : requested;

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
		var iconService = scope.ServiceProvider.GetRequiredService<IWidgetIconService>();

		var resolution = await iconService.Resolve(widgetId, cancellationToken).ConfigureAwait(false);
		var previous = _lastResolution.GetValueOrDefault(widgetId, WidgetIconResolution.Inactive);

		if (resolution == previous)
		{
			return;
		}

		_lastResolution[widgetId] = resolution;
		_renderSignals.RaiseWidgetIconChanged(widgetId);

		if (_deviceSurfaces is not null)
		{
			await _deviceSurfaces.OnWidgetIconChanged(widgetId, cancellationToken).ConfigureAwait(false);
		}
	}
}
