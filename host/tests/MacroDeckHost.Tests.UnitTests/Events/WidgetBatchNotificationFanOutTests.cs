using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Events.Handlers;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Plugins.Capabilities.Callbacks;
using MacroDeckHost.Tests.UnitTests.Plugins;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.Events;

[TestFixture]
public class WidgetBatchNotificationFanOutTests
{
	private RecordingTransport _transport = null!;
	private FakeEventSubscriptionIndex _eventIndex = null!;
	private FakeWidgetVariableIndex _variableIndex = null!;
	private WidgetStateEvalChannel _evalQueue = null!;
	private WidgetDerivedStateStore _derivedState = null!;
	private LabelRenderChannel _renderQueue = null!;
	private VariableRegistry _registry = null!;
	private ServiceProvider _variableServices = null!;
	private IServiceScopeFactory _scopeFactory = null!;
	private CountingWidgetApi _widgetApi = null!;
	private PluginSessionRegistry _sessionRegistry = null!;
	private FakePluginConnection _pluginConnection = null!;
	private HostStatePusher _statePusher = null!;
	private MutableFolderCache _folderCache = null!;
	private WidgetStateReconciler _reconciler = null!;
	private IServiceScope _variableScope = null!;

	[SetUp]
	public void SetUp()
	{
		_transport = new RecordingTransport();
		_eventIndex = new FakeEventSubscriptionIndex();
		_variableIndex = new FakeWidgetVariableIndex();
		_evalQueue = new WidgetStateEvalChannel();
		_derivedState = new WidgetDerivedStateStore();
		_renderQueue = new LabelRenderChannel();
		_widgetApi = new CountingWidgetApi();
		_sessionRegistry = new PluginSessionRegistry(TimeProvider.System, Serilog.Core.Logger.None);
		_pluginConnection = new FakePluginConnection();
		_statePusher
			= new HostStatePusher(_sessionRegistry, new EmptyDeckNavigator(), new EmptyScriptApi(), _widgetApi);
		_folderCache = new MutableFolderCache();

		var services = new ServiceCollection();
		services.AddSingleton<IMediator, RecordingMediator>();
		services.AddSingleton<VariableRegistry>();
		services.AddSingleton<IUserVariableStore, NullUserVariableStore>();
		services.AddTestVariableService();
		_variableServices = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
		_registry = _variableServices.GetRequiredService<VariableRegistry>();
		_scopeFactory = _variableServices.GetRequiredService<IServiceScopeFactory>();

		// IActionButtonStateVariableSync and its background-service initializer are gone (issue #612);
		// WidgetStateReconciler is now the sole writer of vars.state/vars.stateLabel. It only runs when
		// something drains the eval queue it was enqueued to - exactly what
		// WidgetStateEvalBackgroundService does in production, and what ReconcileQueued below does here.
		var readiness = new StartupReadiness();
		readiness.MarkCachesReady();
		readiness.MarkVariablesReady();
		var renderer = new VariableTemplateRenderer(_registry);
		var stateService = new WidgetStateService(_folderCache,
			renderer,
			new ActionConditionEvaluator(renderer),
			new FakeIntegrationRegistry(),
			_derivedState,
			readiness);
		_variableScope = _variableServices.CreateScope();
		_reconciler = new WidgetStateReconciler(stateService,
			_derivedState,
			_variableScope.ServiceProvider.GetRequiredService<IVariableService>(),
			new NoOpFlowExecutor(),
			new NoOpWidgetStatePublisher(),
			_folderCache,
			new NotSupportedWidgetService(),
			new WidgetDataWriteLock(),
			TestLocalization.Preferences,
			TestLocalization.Resolver,
			Serilog.Log.Logger);

		AttachConnectedSession();
	}

	// The eval queue enqueue is only half the story: WidgetStateEvalBackgroundService is what actually
	// drains it and reconciles, which is what turns "enqueued" into vars.state/vars.stateLabel existing.
	// Reconciles by widget id directly (not by draining _evalQueue) so this can run after a test has
	// already asserted the enqueue via AssertEvalQueueEnqueuedAllMembers, which drains it itself.
	private async Task ReconcileAll(IEnumerable<WidgetEntity> widgets)
	{
		foreach (var widget in widgets)
		{
			await _reconciler.Reconcile(widget.Id);
		}
	}

	[TearDown]
	public void TearDown()
	{
		_variableScope.Dispose();
		_variableServices.Dispose();
	}

	[Test]
	public async Task WidgetsCreated_fans_out_to_every_consumer_for_all_members()
	{
		var folderId = Guid.NewGuid();
		var widgets = ThreeToggleButtons(folderId);
		_folderCache.SetWidgets(widgets);
		var notification = new WidgetsCreatedNotification(folderId, widgets);

		await new WidgetsCreatedNotificationHandler(_transport).Handle(notification, CancellationToken.None);
		await new WidgetsCreatedEventIndexHandler(_eventIndex).Handle(notification, CancellationToken.None);
		await new WidgetsCreatedVariableIndexHandler(_variableIndex).Handle(notification, CancellationToken.None);
		await new ActionButtonStateVariableWidgetsCreatedHandler(_evalQueue).Handle(notification,
			CancellationToken.None);
		await _statePusher.Handle(notification, CancellationToken.None);

		AssertIndexesSawAllMembers(widgets);
		AssertEvalQueueEnqueuedAllMembers(widgets);
		await ReconcileAll(widgets);
		AssertActionButtonVariablesRegisteredForAllMembers(widgets);
		AssertOneBatchedTransportEvent<WidgetsCreatedEvent>(evt => evt.Widgets.Select(w => Guid.Parse(w.Id)), widgets);
		AssertOnePluginPush();
	}

	[Test]
	public async Task WidgetsUpdated_fans_out_to_every_consumer_for_all_members_and_requeues_label_renders()
	{
		var folderId = Guid.NewGuid();
		var widgets = ThreeToggleButtons(folderId);
		_folderCache.SetWidgets(widgets);

		await new ActionButtonStateVariableWidgetsCreatedHandler(_evalQueue)
			.Handle(new WidgetsCreatedNotification(folderId, widgets), CancellationToken.None);
		DrainEvalQueue();

		foreach (var widget in widgets)
		{
			widget.Data = "{\"mode\":\"toggle\",\"isToggled\":true}";
		}

		var notification = new WidgetsUpdatedNotification(folderId, widgets);

		await new WidgetsUpdatedNotificationHandler(_transport, _renderQueue).Handle(notification,
			CancellationToken.None);
		await new WidgetsUpdatedEventIndexHandler(_eventIndex).Handle(notification, CancellationToken.None);
		await new WidgetsUpdatedVariableIndexHandler(_variableIndex).Handle(notification, CancellationToken.None);
		await new ActionButtonStateVariableWidgetsUpdatedHandler(_evalQueue).Handle(notification,
			CancellationToken.None);
		await _statePusher.Handle(notification, CancellationToken.None);

		AssertIndexesSawAllMembers(widgets);
		await ReconcileAll(widgets);
		AssertActionButtonVariablesRegisteredForAllMembers(widgets);
		AssertOneBatchedTransportEvent<WidgetsUpdatedEvent>(evt => evt.Widgets.Select(w => Guid.Parse(w.Id)), widgets);
		AssertOnePluginPush();

		// The appearance/label of every member may have changed - each must be requeued for
		// re-render, mirroring what the single-widget handler does per widget (issue #213 step 2).
		Assert.That(DrainRenderQueue(), Is.EquivalentTo(widgets.Select(w => w.Id)));
	}

	[Test]
	public async Task WidgetsDeleted_fans_out_to_every_consumer_for_all_members()
	{
		var folderId = Guid.NewGuid();
		var widgets = ThreeToggleButtons(folderId);
		_folderCache.SetWidgets(widgets);
		await new ActionButtonStateVariableWidgetsCreatedHandler(_evalQueue)
			.Handle(new WidgetsCreatedNotification(folderId, widgets), CancellationToken.None);
		await ReconcileAll(widgets);
		foreach (var widget in widgets)
		{
			Assert.That(Toggled(widget.Id), Is.Not.Null, "precondition: variable exists before delete");
		}

		var widgetIds = widgets.Select(w => w.Id).ToList();
		var notification = new WidgetsDeletedNotification(folderId, widgetIds);

		await new WidgetsDeletedNotificationHandler(_transport).Handle(notification, CancellationToken.None);
		await new WidgetsDeletedEventIndexHandler(_eventIndex).Handle(notification, CancellationToken.None);
		await new WidgetsDeletedVariableIndexHandler(_variableIndex).Handle(notification, CancellationToken.None);
		await new ActionButtonStateVariableWidgetsDeletedHandler(_scopeFactory, _derivedState).Handle(notification,
			CancellationToken.None);
		await _statePusher.Handle(notification, CancellationToken.None);

		Assert.That(_eventIndex.RemovedOwners, Is.EquivalentTo(widgetIds.Select(EventTriggerOwner.ForWidget)));
		Assert.That(_variableIndex.RemovedIds, Is.EquivalentTo(widgetIds));

		foreach (var widgetId in widgetIds)
		{
			Assert.That(Toggled(widgetId), Is.Null, $"toggle-state variable for {widgetId} should be removed");
		}

		AssertOneBatchedTransportEvent<WidgetsDeletedEvent>(evt => evt.WidgetIds.Select(Guid.Parse), widgets);
		AssertOnePluginPush();
	}

	private void AssertIndexesSawAllMembers(IReadOnlyList<WidgetEntity> widgets)
	{
		Assert.That(_eventIndex.ReindexedIds, Is.EquivalentTo(widgets.Select(w => w.Id)));
		Assert.That(_variableIndex.ReindexedIds, Is.EquivalentTo(widgets.Select(w => w.Id)));
	}

	private void AssertActionButtonVariablesRegisteredForAllMembers(IReadOnlyList<WidgetEntity> widgets)
	{
		foreach (var widget in widgets)
		{
			Assert.That(Toggled(widget.Id), Is.Not.Null, $"toggle-state variable missing for widget {widget.Id}");
		}
	}

	private void AssertEvalQueueEnqueuedAllMembers(IReadOnlyList<WidgetEntity> widgets)
		=> Assert.That(DrainEvalQueue(), Is.EquivalentTo(widgets.Select(w => w.Id)));

	private List<Guid> DrainEvalQueue()
	{
		var ids = new List<Guid>();
		while (_evalQueue.Reader.TryRead(out var widgetId))
		{
			ids.Add(widgetId);
		}

		return ids;
	}

	private List<Guid> DrainRenderQueue()
	{
		var ids = new List<Guid>();
		while (_renderQueue.Reader.TryRead(out var widgetId))
		{
			ids.Add(widgetId);
		}

		return ids;
	}

	private void AssertOneBatchedTransportEvent<TEvent>(Func<TEvent, IEnumerable<Guid>> memberIdsOf,
		IReadOnlyList<WidgetEntity> widgets)
		where TEvent : class
	{
		var evt = _transport.Sent.OfType<TEvent>().Single();
		Assert.That(memberIdsOf(evt), Is.EquivalentTo(widgets.Select(w => w.Id)));
	}

	private void AssertOnePluginPush()
	{
		var widgetPushes = _pluginConnection.Sent
			.Where(envelope => envelope.Type == MessageTypes.HostState)
			.Select(envelope =>
				JsonSerializer.Deserialize<HostStatePayload>(envelope.Payload!.Value, PluginProtocolJson.Options))
			.Count(payload => payload?.Api == HostApis.Widgets);

		Assert.That(widgetPushes, Is.EqualTo(1));
		Assert.That(_widgetApi.GetWidgetsCallCount, Is.EqualTo(1));
	}

	private VariableEntity? Toggled(Guid widgetId)
		=> _registry.FindByName(VariableScope.Widget,
			widgetId.ToString(),
			WidgetStateReconciler.StateVariableName);

	private static List<WidgetEntity> ThreeToggleButtons(Guid folderId)
		=> Enumerable.Range(0, 3)
			.Select(_ => new WidgetEntity
			{
				Id = Guid.NewGuid(),
				FolderId = folderId,
				Type = WidgetTypeIds.ActionButton,
				PositionX = 0,
				PositionY = 0,
				Width = 1,
				Height = 1,
				Data = "{\"mode\":\"toggle\",\"isToggled\":false}"
			})
			.ToList();

	private void AttachConnectedSession()
	{
		var record = new PluginSessionRecord
		{
			SessionId = "session-1",
			PluginId = "com.example.plugin",
			DisplayName = "Example Plugin",
			Origin = PluginSessionOrigin.Managed,
			NegotiatedVersion = 1,
			Capabilities = new Dictionary<string, CapabilityNegotiationResult>(StringComparer.Ordinal),
			DeclaredCapabilities = [],
			State = PluginSessionState.Awaiting,
			CreatedAt = TimeProvider.System.GetUtcNow()
		};

		_sessionRegistry.Create(record).GetAwaiter().GetResult();
		var attached = _sessionRegistry.TryAttach(record.SessionId, _pluginConnection, null);
		if (!attached)
		{
			throw new InvalidOperationException("TryAttach failed in test setup.");
		}
	}

	private sealed class NullUserVariableStore : IUserVariableStore
	{
		public IReadOnlyList<VariableEntity> Load() => [];

		public void Save(IEnumerable<VariableEntity> userVariables)
		{
		}
	}

	private sealed class NoOpFlowExecutor : IFlowExecutor
	{
		public Task<FlowExecutionResult> ExecuteAsync(FlowExecutionRequest request, CancellationToken cancellationToken)
			=> Task.FromResult(new FlowExecutionResult
			{
				ExecutionId = Guid.NewGuid(),
				Status = FlowExecutionStatus.Succeeded,
				MatchedFlows = 0
			});
	}

	private sealed class NotSupportedWidgetService : IWidgetService
	{
		public Task<Result<WidgetEntity, WidgetError>> Update(WidgetEntity widget) => throw new NotSupportedException();

		public Task<Result<WidgetEntity, WidgetError>> Create(Guid folderId,
			WidgetEntity widget,
			Guid? sourceWidgetId = null)
			=> throw new NotSupportedException();

		public Task<Result<List<WidgetEntity>, WidgetError>> UpdatePositions(
			Guid folderId,
			IReadOnlyList<WidgetPlacement> placements)
			=> throw new NotSupportedException();

		public Task<Result<WidgetError>> Delete(Guid widgetId, Guid folderId) => throw new NotSupportedException();

		public Task<Result<WidgetEntity, WidgetError>> SetPinned(Guid folderId,
			Guid widgetId,
			bool pinned,
			PinScope? scope = null)
			=> throw new NotSupportedException();

		public Task<Result<List<WidgetEntity>, WidgetError>> CreateMany(Guid folderId,
			IReadOnlyList<WidgetEntity> widgets,
			IReadOnlyList<Guid>? replaceIds = null,
			IReadOnlyList<Guid?>? sourceWidgetIds = null)
			=> throw new NotSupportedException();

		public Task<Result<WidgetError>> DeleteMany(Guid folderId, IReadOnlyList<Guid> widgetIds)
			=> throw new NotSupportedException();

		public Task<Result<List<WidgetEntity>, WidgetError>> SetPinnedMany(
			Guid folderId,
			IReadOnlyList<Guid> widgetIds,
			bool pinned,
			PinScope? scope = null)
			=> throw new NotSupportedException();
	}

	private sealed class MutableFolderCache : IFolderCache
	{
		private FolderEntity _folder = new() { Name = "f", Order = 0, Widgets = [] };

		public void SetWidgets(IReadOnlyList<WidgetEntity> widgets)
			=> _folder = new FolderEntity { Id = _folder.Id, Name = "f", Order = 0, Widgets = widgets.ToList() };

		public List<FolderEntity> GetAllFolders() => [_folder];
		public FolderEntity? GetFolderById(Guid id) => _folder;
		public Task InitializeCache() => Task.CompletedTask;
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

	private sealed class RecordingTransport : IUiTransport
	{
		public Task SendToConnection<T>(string connectionId, T message, CancellationToken cancellationToken = default)
			where T : class
			=> Task.CompletedTask;

		public Task AddToGroup(string connectionId, string group, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;

		public Task RemoveFromGroup(string connectionId, string group, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;

		public List<object> Sent { get; } = [];

		public Task Send<T>(T message, CancellationToken cancellationToken = default)
			where T : class
		{
			Sent.Add(message);
			return Task.CompletedTask;
		}

		public Task SendToGroup<T>(string group, T message, CancellationToken cancellationToken = default)
			where T : class
		{
			Sent.Add(message);
			return Task.CompletedTask;
		}
	}

	private sealed class FakeEventSubscriptionIndex : IEventSubscriptionIndex
	{
		public List<Guid> ReindexedIds { get; } = [];

		public List<EventTriggerOwner> RemovedOwners { get; } = [];

		public bool HasSubscribers(string qualifiedEventId) => false;

		public IReadOnlyList<EventSubscription> Find(string qualifiedEventId) => [];

		public EventSubscription? Find(EventTarget target) => null;

		public IReadOnlyList<EventSubscription> FindByProvider(string providerId) => [];

		public event Action? Changed
		{
			add { }
			remove { }
		}

		public void Rebuild()
		{
		}

		public void ReindexWidget(Guid widgetId, string? widgetData) => ReindexedIds.Add(widgetId);

		public void ReindexAutomation(Guid automationId, string? flows, bool enabled)
		{
		}

		public void Remove(EventTriggerOwner owner) => RemovedOwners.Add(owner);
	}

	private sealed class FakeWidgetVariableIndex : IWidgetVariableIndex
	{
		public List<Guid> ReindexedIds { get; } = [];

		public List<Guid> RemovedIds { get; } = [];

		public IReadOnlyList<Guid> FindLabelReferences(string variableName) => [];

		public IReadOnlyList<Guid> FindStateMappingReferences(string variableName) => [];

		public IReadOnlyList<Guid> FindProviderReferences(string integrationId, string? actionId = null) => [];

		public IReadOnlyList<Guid> FindIconProviderReferences(string integrationId, string? actionId = null) => [];

		public bool LabelReferences(Guid widgetId, string variableName) => false;

		public bool StateMappingReferences(Guid widgetId, string variableName) => false;

		public void Rebuild()
		{
		}

		public void ReindexWidget(Guid widgetId, string type, string? data) => ReindexedIds.Add(widgetId);

		public void Remove(Guid widgetId) => RemovedIds.Add(widgetId);
	}

	private sealed class CountingWidgetApi : IWidgetApi
	{
		public int GetWidgetsCallCount { get; private set; }

		public IReadOnlyList<WidgetTargetInfo> GetWidgets()
		{
			GetWidgetsCallCount++;
			return [];
		}

		public bool Exists(string widgetId) => false;

		public Task<bool> ApplyAsync(WidgetAppearanceRequest request, CancellationToken cancellationToken = default)
			=> Task.FromResult(false);

		public Task<WidgetStateWriteResult> SetStateAsync(
			string widgetId,
			string stateId,
			CancellationToken cancellationToken = default)
			=> Task.FromResult(WidgetStateWriteResult.Failed(WidgetStateWriteError.NotFound));

		public Task<WidgetStateWriteResult> AdvanceStateAsync(string widgetId,
			CancellationToken cancellationToken = default)
			=> Task.FromResult(WidgetStateWriteResult.Failed(WidgetStateWriteError.NotFound));
	}

	private sealed class EmptyDeckNavigator : IDeckNavigator
	{
		public Task ChangeFolderAsync(string folderId,
			string? originClientId = null,
			CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task ChangeProfileAsync(string profileId,
			string? originClientId = null,
			CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task GoToParentAsync(string? originClientId = null, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;

		public Task GoBackAsync(string? originClientId = null, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;

		public IReadOnlyList<DeckFolder> GetFolders() => [];

		public IReadOnlyList<DeckProfile> GetProfiles() => [];
	}

	private sealed class EmptyScriptApi : IScriptApi
	{
		public IReadOnlyList<Script> GetScripts() => [];

		public Task<MacroDeck.Sdk.Actions.ActionResult> RunAsync(string scriptId,
			IReadOnlyDictionary<string, object?>? inputs = null,
			string? originClientId = null,
			string? ownerWidgetId = null,
			CancellationToken cancellationToken = default)
			=> MacroDeck.Sdk.Actions.ActionResult.SucceededTask;
	}
}
