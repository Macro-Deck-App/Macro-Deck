using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Infrastructure.BackgroundServices;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MacroDeckHost.Tests.UnitTests.BackgroundServices;

/// <summary>
/// <see cref="WidgetIconProviderPollService" /> mirrors <c>WidgetStateEvalBackgroundService</c>'s
/// scheduling shape but is kept a separate type entirely (issue #425) - these tests exercise its
/// scheduling decision (<see cref="WidgetIconProviderPollService.EnqueueDueProviders" />) and queue
/// processing (<see cref="WidgetIconProviderPollService.ProcessQueuedAsync" />) directly, without a real
/// timer, exactly as <c>IntegrationVariablePollingBackgroundServiceTests</c> drives its own service's
/// <c>DispatchDue</c>.
/// </summary>
[TestFixture]
internal sealed class WidgetIconProviderPollServiceTests
{
	private static WidgetEntity Button(Guid id) => new()
	{
		Id = id,
		FolderId = Guid.NewGuid(),
		Type = WidgetTypeIds.ActionButton,
		// Only the text prefilter cares about this: it must mention "iconProvider" or EnqueueDueProviders
		// rules the widget out before ever asking the icon service for its poll interval.
		Data = """{"iconProvider":{"blockId":"blk-1"}}"""
	};

	// Acceptance Group F, scenario 24 (counterexample): InvalidateIconAsync fans out by action id to
	// exactly the matching widgets, promptly - not everything, not only the first match, and the queue is
	// actually drained rather than merely having a timer reset.
	[Test]
	public async Task Invalidate_ReachesExactlyTheWidgetsFollowingThatAction_AndNoOthers()
	{
		var w1 = Guid.NewGuid();
		var w2 = Guid.NewGuid();
		var w3 = Guid.NewGuid();

		var variableIndex = new FakeWidgetVariableIndex();
		variableIndex.IconProviderReferencesByAction[("spotify", "current-track")] = [w1, w2];
		variableIndex.IconProviderReferencesByAction[("spotify", "weather-icon")] = [w3];

		var queue = new WidgetIconEvalChannel();
		var invalidator = new WidgetIconInvalidator(variableIndex, queue);
		var iconService = new FakeWidgetIconServiceForPoll();
		var folderCache = new FakeFolderCache(Button(w1), Button(w2), Button(w3));
		var service = CreateService(queue, iconService, folderCache);

		invalidator.Invalidate("spotify", "current-track");
		await service.ProcessQueuedAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(iconService.ResolveCallCounts.GetValueOrDefault(w1), Is.EqualTo(1));
			Assert.That(iconService.ResolveCallCounts.GetValueOrDefault(w2), Is.EqualTo(1));
			Assert.That(iconService.ResolveCallCounts.GetValueOrDefault(w3),
				Is.EqualTo(0),
				"an unrelated widget is never touched");
		});
	}

	// Acceptance Group F, scenario 25 (first half): nothing is polled while nothing displays the widget.
	// The very first tick after startup always seeds every icon-provider button once (mirroring
	// WidgetStateEvalBackgroundService's own SeedExistingButtons, which this scheduling logic shares);
	// what scenario 25 asserts is the steady state after that: with nobody subscribed, the widget backs
	// off to the idle floor rather than staying due on literally every tick.
	[Test]
	public void NothingSubscribed_MeansTheNextPollBacksOffToTheIdleFloor_RatherThanStayingDueEveryTick()
	{
		var widgetId = Guid.NewGuid();
		var iconService = new FakeWidgetIconServiceForPoll();
		iconService.PollIntervalByWidget[widgetId] = TimeSpan.FromMilliseconds(1);

		var queue = new WidgetIconEvalChannel();
		var folderCache = new FakeFolderCache(Button(widgetId));
		var service = CreateService(queue, iconService, folderCache, new WidgetStateSubscriptionTracker());

		service.EnqueueDueProviders();
		Assert.That(queue.Reader.TryRead(out _), Is.True, "the first tick always seeds every provider once");

		service.EnqueueDueProviders();

		Assert.That(queue.Reader.TryRead(out _),
			Is.False,
			"with nobody subscribed, the next poll backs off to the idle floor instead of the declared 1 ms");
	}

	// Acceptance Group F, scenario 25 (second half): once a client opens the widget, it becomes (and
	// stays) due at the declared interval - clamped to the host's published floor, never honoured
	// literally, so an immediate second tick does not re-enqueue it either.
	[Test]
	public void OnceSubscribed_TheWidgetStaysDue_ButNeverFasterThanThePublishedClampFloor()
	{
		var widgetId = Guid.NewGuid();
		var iconService = new FakeWidgetIconServiceForPoll();

		// Declared far below the published floor - if the clamp were not applied, a second tick taken
		// immediately after the first would still be due, rather than respecting the floor.
		Assert.That(WidgetIconProviderPollService.MinPollInterval, Is.GreaterThan(TimeSpan.FromMilliseconds(1)));
		iconService.PollIntervalByWidget[widgetId] = TimeSpan.FromMilliseconds(1);

		var queue = new WidgetIconEvalChannel();
		var subscriptions = new WidgetStateSubscriptionTracker();
		subscriptions.Add("conn-1", widgetId.ToString());
		var folderCache = new FakeFolderCache(Button(widgetId));
		var service = CreateService(queue, iconService, folderCache, subscriptions);

		service.EnqueueDueProviders();
		Assert.That(queue.Reader.TryRead(out var firstDequeue), Is.True);

		service.EnqueueDueProviders();

		Assert.Multiple(() =>
		{
			Assert.That(firstDequeue, Is.EqualTo(widgetId));
			Assert.That(queue.Reader.TryRead(out _),
				Is.False,
				"the declared 1 ms interval is clamped to the published floor, not honoured literally");
		});
	}

	private static WidgetIconProviderPollService CreateService(
		WidgetIconEvalChannel queue,
		FakeWidgetIconServiceForPoll iconService,
		FakeFolderCache folderCache,
		WidgetStateSubscriptionTracker? subscriptions = null)
	{
		var serviceProvider = new ServiceCollection()
			.AddScoped<IWidgetIconService>(_ => iconService)
			.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

		return new WidgetIconProviderPollService(new StartedHostLifetime(),
			queue,
			serviceProvider.GetRequiredService<IServiceScopeFactory>(),
			new RecordingRenderSignals(),
			subscriptions ?? new WidgetStateSubscriptionTracker(),
			folderCache,
			ReadyStartup(),
			Serilog.Log.Logger);
	}

	private static Application.Services.StartupReadiness ReadyStartup()
	{
		var readiness = new Application.Services.StartupReadiness();
		readiness.MarkCachesReady();
		readiness.MarkVariablesReady();
		return readiness;
	}

	private sealed class FakeFolderCache : IFolderCache
	{
		private readonly FolderEntity _folder;

		public FakeFolderCache(params WidgetEntity[] widgets)
		{
			_folder = new FolderEntity { Name = "f", Order = 0, Widgets = widgets.ToList() };
		}

		public List<FolderEntity> GetAllFolders() => [_folder];

		public Task InitializeCache() => Task.CompletedTask;
		public FolderEntity? GetFolderById(Guid id) => _folder;
		public List<FolderEntity> GetFoldersByParentId(Guid? parentId) => [_folder];
		public List<FolderEntity> GetFoldersByProfileId(Guid profileId) => [_folder];
		public Task AddOrUpdate(FolderEntity folder) => Task.CompletedTask;

		public Task AddOrUpdateRange(IReadOnlyCollection<FolderEntity> folders) => Task.CompletedTask;

		public Task<FolderSubtreeRemoval> RemoveSubtree(Guid rootId)
			=> Task.FromResult(new FolderSubtreeRemoval(false, [], []));

		public void AddWidget(Guid folderId, WidgetEntity widget)
		{
		}

		public void AddWidgets(Guid folderId, IReadOnlyList<WidgetEntity> widgets)
		{
		}

		public void UpdateWidget(Guid folderId, WidgetEntity widget)
		{
		}

		public void UpdateWidgets(Guid folderId, IReadOnlyList<WidgetEntity> widgets)
		{
		}

		public void UpdateWidgetPositions(Guid folderId, IReadOnlyList<WidgetPlacement> placements)
		{
		}

		public void RemoveWidget(Guid folderId, Guid widgetId)
		{
		}

		public void RemoveWidgets(Guid folderId, IReadOnlyList<Guid> widgetIds)
		{
		}

		public void ReplaceWidgets(Guid folderId, IReadOnlyList<Guid> removeIds, IReadOnlyList<WidgetEntity> addWidgets)
		{
		}
	}

	private sealed class FakeWidgetIconServiceForPoll : IWidgetIconService
	{
		public Dictionary<Guid, TimeSpan> PollIntervalByWidget { get; } = new();
		public Dictionary<Guid, int> ResolveCallCounts { get; } = new();

		public Task<WidgetIconResolution> Resolve(Guid widgetId, CancellationToken cancellationToken = default)
		{
			ResolveCallCounts[widgetId] = ResolveCallCounts.GetValueOrDefault(widgetId) + 1;
			return Task.FromResult(WidgetIconResolution.Inactive);
		}

		public TimeSpan? GetProviderPollInterval(Guid widgetId)
			=> PollIntervalByWidget.TryGetValue(widgetId, out var interval) ? interval : null;
	}

	private sealed class FakeWidgetVariableIndex : IWidgetVariableIndex
	{
		public Dictionary<(string IntegrationId, string ActionId), IReadOnlyList<Guid>> IconProviderReferencesByAction
		{
			get;
		} = new();

		public IReadOnlyList<Guid> FindLabelReferences(string variableName) => [];

		public IReadOnlyList<Guid> FindStateMappingReferences(string variableName) => [];

		public IReadOnlyList<Guid> FindProviderReferences(string integrationId, string? actionId = null) => [];

		public IReadOnlyList<Guid> FindIconProviderReferences(string integrationId, string? actionId = null)
		{
			if (actionId is null)
			{
				return IconProviderReferencesByAction
					.Where(pair => string.Equals(pair.Key.IntegrationId, integrationId, StringComparison.Ordinal))
					.SelectMany(pair => pair.Value)
					.Distinct()
					.ToList();
			}

			return IconProviderReferencesByAction.TryGetValue((integrationId, actionId), out var widgets)
				? widgets
				: [];
		}

		public bool LabelReferences(Guid widgetId, string variableName) => false;

		public bool StateMappingReferences(Guid widgetId, string variableName) => false;

		public void Rebuild()
		{
		}

		public void ReindexWidget(Guid widgetId, string type, string? data)
		{
		}

		public void Remove(Guid widgetId)
		{
		}
	}

	private sealed class StartedHostLifetime : IHostApplicationLifetime
	{
		public CancellationToken ApplicationStarted { get; } = new(canceled: true);
		public CancellationToken ApplicationStopping => CancellationToken.None;
		public CancellationToken ApplicationStopped => CancellationToken.None;

		public void StopApplication()
		{
		}
	}
}
