using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Deck;
using MacroDeckHost.Application.Profiles;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using MacroDeckHost.Application.Ui.Transport.Messages.Folders;
using MacroDeckHost.Application.Ui.Transport.Messages.Profiles;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.Profiles;
using MacroDeck.Sdk.Widgets;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests;

public class ExecuteActionButtonTriggerRequestMessageHandlerTests
{
	private static readonly Guid _folderId = Guid.NewGuid();
	private static readonly Guid _widgetId = Guid.NewGuid();

	private FakeFolderCache _folders = null!;
	private FakeProfileRegistry _profiles = null!;
	private FakeActionExecutionCoordinator _coordinator = null!;
	private FakeHostLockState _lockState = null!;
	private ExecuteActionButtonTriggerRequestMessageHandler _handler = null!;

	[SetUp]
	public void SetUp()
	{
		_folders = new FakeFolderCache(ActionButtonWidget());
		_profiles = new FakeProfileRegistry();
		_coordinator = new FakeActionExecutionCoordinator();
		_lockState = new FakeHostLockState();
		_handler = CreateHandler(_folders, _coordinator, new NoOpActionButtonStateService());
	}

	// Issue #718: pressing an action button with no state provider and no state mapping must advance
	// its state, wrapping past the last one back to the first - but only on onShortPress, only when
	// neither authority is configured, and only while the button has not turned cycling off. These
	// scenarios wire up the real ActionButtonStateService (not a fake) so the guard it already enforces
	// - refuse rather than advance while a provider or mapping is authoritative - is exercised for real
	// through the handler that every client presses through.
	private const string ThreeStatesOnFirst =
		"{\"stateMode\":true,\"states\":[{\"id\":\"a\",\"label\":\"A\"},{\"id\":\"b\",\"label\":\"B\"}," +
		"{\"id\":\"c\",\"label\":\"C\"}],\"activeStateId\":\"a\"}";

	private const string ThreeStatesOnLast =
		"{\"stateMode\":true,\"states\":[{\"id\":\"a\",\"label\":\"A\"},{\"id\":\"b\",\"label\":\"B\"}," +
		"{\"id\":\"c\",\"label\":\"C\"}],\"activeStateId\":\"c\"}";

	private const string ThreeStatesWithMapping =
		"{\"stateMode\":true,\"states\":[{\"id\":\"a\",\"label\":\"A\"},{\"id\":\"b\",\"label\":\"B\"}," +
		"{\"id\":\"c\",\"label\":\"C\"}],\"activeStateId\":\"a\",\"stateMapping\":{\"rules\":[{\"id\":\"r1\"," +
		"\"stateId\":\"b\",\"when\":{\"kind\":\"compare\",\"left\":{\"$var\":\"cpu\"},\"operator\":\">=\"," +
		"\"right\":90}}],\"fallbackStateId\":\"a\"}}";

	private const string ThreeStatesWithProvider =
		"{\"stateMode\":true,\"states\":[{\"id\":\"a\",\"label\":\"A\"},{\"id\":\"b\",\"label\":\"B\"}," +
		"{\"id\":\"c\",\"label\":\"C\"}],\"activeStateId\":\"a\",\"stateProvider\":{\"blockId\":\"blk-1\"," +
		"\"integrationId\":\"i\",\"actionId\":\"a\",\"states\":[{\"id\":\"a\",\"label\":\"A\"}]}}";

	[Test]
	public async Task A_short_press_advances_a_state_mode_button_to_the_next_state()
	{
		var folders = new FakeFolderCache(ActionButtonWidget());
		var (handler, widgets, _) = CreateStateHandler(folders, ThreeStatesOnFirst);

		var response = await handler.Handle(Request(triggerType: WidgetTriggerTypes.ShortPress),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.True);
			Assert.That(widgets.Updated?.Data, Does.Contain("\"activeStateId\":\"b\""));
		});
	}

	[Test]
	public async Task A_short_press_on_the_last_state_wraps_back_to_the_first_state()
	{
		var folders = new FakeFolderCache(ActionButtonWidget());
		var (handler, widgets, _) = CreateStateHandler(folders, ThreeStatesOnLast);

		var response = await handler.Handle(Request(triggerType: WidgetTriggerTypes.ShortPress),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.True);
			Assert.That(widgets.Updated?.Data, Does.Contain("\"activeStateId\":\"a\""));
		});
	}

	[Test]
	public async Task A_short_press_leaves_state_unchanged_when_the_button_turned_cycling_off()
	{
		var folders = new FakeFolderCache(ActionButtonWidget());
		var (handler, widgets, _) = CreateStateHandler(folders,
			ThreeStatesOnFirst.Replace("\"stateMode\":true,",
				"\"stateMode\":true,\"cycleStatesOnPress\":false,",
				StringComparison.Ordinal));

		var response = await handler.Handle(Request(triggerType: WidgetTriggerTypes.ShortPress),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.True);
			Assert.That(widgets.Updated, Is.Null, "cycling was turned off, so a press writes nothing");
		});
	}

	[Test]
	public async Task A_short_press_leaves_state_unchanged_when_a_state_mapping_is_configured()
	{
		var folders = new FakeFolderCache(ActionButtonWidget());
		var (handler, widgets, _) = CreateStateHandler(folders, ThreeStatesWithMapping);

		var response = await handler.Handle(Request(triggerType: WidgetTriggerTypes.ShortPress),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.True, "a refused advance must not surface as a press failure");
			Assert.That(widgets.Updated, Is.Null, "the mapping is authoritative, so nothing is written");
		});
	}

	[Test]
	public async Task A_short_press_leaves_state_unchanged_when_a_state_provider_is_configured()
	{
		var folders = new FakeFolderCache(ActionButtonWidget());
		var (handler, widgets, _) = CreateStateHandler(folders, ThreeStatesWithProvider);

		var response = await handler.Handle(Request(triggerType: WidgetTriggerTypes.ShortPress),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.True, "a refused advance must not surface as a press failure");
			Assert.That(widgets.Updated, Is.Null, "the provider is authoritative, so nothing is written");
		});
	}

	[Test]
	public async Task A_long_press_does_not_advance_state()
	{
		var folders = new FakeFolderCache(ActionButtonWidget());
		var (handler, widgets, _) = CreateStateHandler(folders, ThreeStatesOnFirst);

		var response = await handler.Handle(Request(triggerType: "onLongPress"), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.True);
			Assert.That(widgets.Updated, Is.Null, "only onShortPress may advance state");
		});
	}

	[Test]
	public async Task A_short_press_on_a_button_not_in_state_mode_succeeds_and_writes_nothing()
	{
		var folders = new FakeFolderCache(ActionButtonWidget());
		var (handler, widgets, _) = CreateStateHandler(folders, "{}");

		var response = await handler.Handle(Request(triggerType: WidgetTriggerTypes.ShortPress),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.True);
			Assert.That(widgets.Updated, Is.Null);
		});
	}

	[Test]
	public async Task The_press_flow_dispatch_observes_vars_state_already_switched_to_the_new_state()
	{
		var folders = new FakeFolderCache(ActionButtonWidget());
		folders.SetWidgetData(_widgetId, ThreeStatesOnFirst);
		var stateService = CreateRealStateService(folders, out _, out var vars);

		string? observedAtDispatch = null;
		var coordinator = new FakeActionExecutionCoordinator
		{
			OnRun = () => observedAtDispatch =
				vars.Upserts.LastOrDefault(u => u.Name == WidgetStateReconciler.StateVariableName).Value as string
		};

		var handler = CreateHandler(folders, coordinator, stateService);

		await handler.Handle(Request(triggerType: WidgetTriggerTypes.ShortPress), CancellationToken.None);

		Assert.That(observedAtDispatch,
			Is.EqualTo("b"),
			"the press flow must see the state this press just switched to, not the one it started on");
	}

	private ExecuteActionButtonTriggerRequestMessageHandler CreateHandler(
		FakeFolderCache folders,
		FakeActionExecutionCoordinator coordinator,
		IActionButtonStateService stateService)
		=> new(folders,
			_profiles,
			_lockState,
			CreateTriggerService(coordinator, stateService),
			new WidgetTypeRegistry(new RecordingMediator()));

	private static ExecuteActionButtonTriggerRequestMessageHandler CreateStandaloneHandler(
		FakeFolderCache folders,
		FakeActionExecutionCoordinator coordinator,
		IActionButtonStateService stateService)
		=> new(folders,
			new FakeProfileRegistry(),
			new FakeHostLockState(),
			CreateTriggerService(coordinator, stateService),
			new WidgetTypeRegistry(new RecordingMediator()));

	// WidgetTriggerService resolves its scoped IActionButtonStateService through a scope it opens itself
	// (see the type's own doc comment) - built against a tiny real ServiceProvider here rather than a
	// fake IServiceScopeFactory, matching ActionExecutionCoordinatorTests' precedent for the same shape
	// of dependency. The container is never disposed: these tests are short-lived and its single
	// registration holds no unmanaged state.
	private static WidgetTriggerService CreateTriggerService(
		FakeActionExecutionCoordinator coordinator,
		IActionButtonStateService stateService)
	{
		var services = new ServiceCollection().AddSingleton(stateService).BuildServiceProvider();

		return new WidgetTriggerService(services.GetRequiredService<IServiceScopeFactory>(),
			coordinator,
			Serilog.Log.Logger);
	}

	private static (ExecuteActionButtonTriggerRequestMessageHandler Handler,
		RecordingWidgetService Widgets,
		RecordingVariableService Vars) CreateStateHandler(FakeFolderCache folders, string widgetData)
	{
		folders.SetWidgetData(_widgetId, widgetData);
		var stateService = CreateRealStateService(folders, out var widgets, out var vars);
		var coordinator = new FakeActionExecutionCoordinator();
		var handler = CreateStandaloneHandler(folders, coordinator, stateService);
		return (handler, widgets, vars);
	}

	// Builds the real ActionButtonStateService (not a fake), against the given fake folder cache, so
	// the provider/mapping/state-mode guard it already enforces is exercised for real through the
	// handler - not re-asserted against a stand-in that could silently drift from it.
	private static ActionButtonStateService CreateRealStateService(
		FakeFolderCache folders,
		out RecordingWidgetService widgets,
		out RecordingVariableService vars)
	{
		widgets = new RecordingWidgetService();
		vars = new RecordingVariableService();
		var writeLock = new WidgetDataWriteLock();
		var registry = new VariableRegistry();
		var renderer = new VariableTemplateRenderer(registry);
		var evaluator = new ActionConditionEvaluator(renderer);
		var readiness = new StartupReadiness();
		readiness.MarkCachesReady();
		readiness.MarkVariablesReady();

		// Startup.cs registers WidgetDerivedStateStore as a single shared singleton - share one instance
		// here too, rather than one per collaborator, so this "real graph" reflects that.
		var derivedStateStore = new WidgetDerivedStateStore();
		var stateService = new WidgetStateService(folders,
			renderer,
			evaluator,
			new FakeIntegrationRegistry(),
			derivedStateStore,
			readiness);
		var reconciler = new WidgetStateReconciler(stateService,
			derivedStateStore,
			vars,
			new NoOpFlowExecutor(),
			new NoOpWidgetStatePublisher(),
			folders,
			widgets,
			writeLock,
			TestLocalization.Preferences,
			TestLocalization.Resolver,
			Serilog.Log.Logger);

		return new ActionButtonStateService(folders, writeLock, widgets, reconciler);
	}

	[TearDown]
	public void TearDown() => _coordinator.Dispose();

	private static WidgetEntity ActionButtonWidget(string type = WidgetTypeIds.ActionButton)
		=> new() { Id = _widgetId, FolderId = _folderId, Type = type, Data = "{}" };

	private static ExecuteActionButtonTriggerRequest Request(
		string triggerType = "onShortPress",
		string? widgetId = null,
		string? folderId = null,
		string? clientId = "client-1")
		=> new()
		{
			TriggerType = triggerType,
			WidgetId = widgetId ?? _widgetId.ToString(),
			FolderId = folderId ?? _folderId.ToString(),
			ClientId = clientId
		};

	[Test]
	public async Task A_client_supplied_device_origin_never_reaches_the_flow()
	{
		var response = await _handler.Handle(Request(clientId: DeviceOrigin.For(Guid.NewGuid())),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.True);
			Assert.That(_coordinator.LastRequest?.OriginClientId,
				Is.Null,
				"a client must not be able to run a flow as if it were a device session");
		});
	}

	[Test]
	public async Task A_press_the_host_attributes_to_a_device_runs_under_that_devices_origin()
	{
		var deviceId = Guid.NewGuid();
		var request = Request(clientId: null!);
		request.OriginDeviceId = deviceId;

		await _handler.Handle(request, CancellationToken.None);

		Assert.That(_coordinator.LastRequest?.OriginClientId, Is.EqualTo(DeviceOrigin.For(deviceId)));
	}

	[Test]
	public async Task Empty_trigger_type_is_rejected()
	{
		var response = await _handler.Handle(Request(triggerType: ""), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Status, Is.EqualTo(ActionExecutionStatus.Failed));
			Assert.That(response.Error?.Code, Is.EqualTo("VALIDATION_ERROR"));
		});
	}

	[Test]
	public async Task A_button_press_is_refused_while_locked_and_no_flow_is_started()
	{
		_lockState.IsLocked = true;

		var response = await _handler.Handle(Request(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Status, Is.EqualTo(ActionExecutionStatus.Failed));
			Assert.That(response.Error?.Code, Is.EqualTo(ActionExecutionErrorCodes.HostLocked));
			Assert.That(_coordinator.Runs, Is.Zero);
		});
	}

	[Test]
	public async Task A_virtual_widget_press_is_not_routed_to_its_plugin_while_locked()
	{
		_profiles.Virtual = true;
		_lockState.IsLocked = true;

		var response = await _handler.Handle(Request(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Error?.Code, Is.EqualTo(ActionExecutionErrorCodes.HostLocked));
			Assert.That(_profiles.Routed, Is.Zero);
		});
	}

	[Test]
	public async Task A_button_press_runs_again_once_the_host_unlocks()
	{
		_lockState.IsLocked = true;
		await _handler.Handle(Request(), CancellationToken.None);

		_lockState.IsLocked = false;
		var response = await _handler.Handle(Request(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.True);
			Assert.That(_coordinator.Runs, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task An_onEvent_trigger_type_is_rejected()
	{
		var response = await _handler.Handle(Request(triggerType: WidgetTriggerTypes.Event), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error?.Code, Is.EqualTo("VALIDATION_ERROR"));
		});
	}

	[Test]
	public async Task An_unparseable_widget_id_is_rejected()
	{
		var response = await _handler.Handle(Request(widgetId: "not-a-guid"), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error?.Code, Is.EqualTo("VALIDATION_ERROR"));
		});
	}

	[Test]
	public async Task An_unparseable_folder_id_is_rejected()
	{
		var response = await _handler.Handle(Request(folderId: "not-a-guid"), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error?.Code, Is.EqualTo("VALIDATION_ERROR"));
		});
	}

	[Test]
	public async Task An_unknown_widget_is_not_found()
	{
		var response = await _handler.Handle(Request(widgetId: Guid.NewGuid().ToString()), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error?.Code, Is.EqualTo("NOT_FOUND"));
		});
	}

	// Issue #748 migrated the Action Button onto Macro Deck UI sessions, but this REST/WebSocket handler
	// still has to serve every non-Action-Button tile that presses through it - MusicPlayer, Weather,
	// HistoryGraph and Clock - and an older or non-Angular client that never adopted a UI session for an
	// ActionButton either. Both keep going through IWidgetTriggerService exactly like an ActionButton
	// press does.
	[TestCase(WidgetTypeIds.MusicPlayer)]
	[TestCase(WidgetTypeIds.Weather)]
	[TestCase(WidgetTypeIds.HistoryGraph)]
	[TestCase(WidgetTypeIds.Clock)]
	public async Task A_non_action_button_tile_still_runs_its_flow_through_this_handler(string widgetType)
	{
		_folders = new FakeFolderCache(ActionButtonWidget(widgetType));
		_handler = CreateHandler(_folders, _coordinator, new NoOpActionButtonStateService());

		var response = await _handler.Handle(Request(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.True);
			Assert.That(_coordinator.Runs, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task An_action_button_press_from_an_old_client_with_no_ui_session_still_runs_its_flow()
	{
		// An old or non-Angular client never opens a UI session and only ever calls this handler - the
		// same request shape a current client's ActionButtonWidgetSession never has to make.
		var response = await _handler.Handle(Request(triggerType: WidgetTriggerTypes.ShortPress),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.True);
			Assert.That(_coordinator.Runs, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task A_widget_type_that_does_not_support_triggers_is_rejected()
	{
		_folders = new FakeFolderCache(ActionButtonWidget(WidgetTypeIds.Slider));
		_handler = CreateHandler(_folders, _coordinator, new NoOpActionButtonStateService());

		var response = await _handler.Handle(Request(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error?.Code, Is.EqualTo("VALIDATION_ERROR"));
		});
	}

	[Test]
	public async Task A_succeeding_flow_reports_success_with_a_duration()
	{
		_coordinator.InlineResult = new FlowExecutionResult
		{
			ExecutionId = Guid.NewGuid(),
			Status = FlowExecutionStatus.Succeeded,
			DurationMs = 42,
			MatchedFlows = 1
		};

		var response = await _handler.Handle(Request(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.True);
			Assert.That(response.Status, Is.EqualTo(ActionExecutionStatus.Succeeded));
			Assert.That(response.DurationMs, Is.EqualTo(42));
		});
	}

	[Test]
	public async Task A_failing_flow_reports_failure_with_its_code_and_message()
	{
		_coordinator.InlineResult = new FlowExecutionResult
		{
			ExecutionId = Guid.NewGuid(),
			Status = FlowExecutionStatus.Failed,
			MatchedFlows = 1,
			ErrorCode = "PROVIDER_ERROR",
			ErrorMessage = "It broke."
		};

		var response = await _handler.Handle(Request(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Status, Is.EqualTo(ActionExecutionStatus.Failed));
			Assert.That(response.Error?.Code, Is.EqualTo("PROVIDER_ERROR"));
			Assert.That(TestLocalization.Resolve(response.Error?.Message), Is.EqualTo("It broke."));
		});
	}

	[Test]
	public async Task A_flow_that_outlives_the_bound_answers_accepted_with_an_execution_id()
	{
		_coordinator.Slow = true;

		var response = await _handler.Handle(Request(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.True);
			Assert.That(response.Status, Is.EqualTo(ActionExecutionStatus.Accepted));
			Assert.That(response.ExecutionId, Is.Not.Empty);
		});
	}

	private sealed class FakeFolderCache : IFolderCache
	{
		private readonly FolderEntity _folder;

		public FakeFolderCache(params WidgetEntity[] widgets)
			=> _folder = new FolderEntity { Id = _folderId, Name = "f", Order = 0, Widgets = widgets.ToList() };

		public void SetWidgetData(Guid widgetId, string data)
		{
			var widget = _folder.Widgets.First(w => w.Id == widgetId);
			widget.Data = data;
		}

		public FolderEntity? GetFolderById(Guid id) => id == _folder.Id ? _folder : null;
		public List<FolderEntity> GetAllFolders() => [_folder];
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

	private sealed class FakeProfileRegistry : IProfileRegistry
	{
		public bool Virtual { get; set; }

		public int Routed { get; private set; }

		public IReadOnlyList<Profile> GetProfiles() => [];
		public IReadOnlyList<Folder> GetFoldersForProfile(string profileId) => [];
		public bool IsVirtual(string profileId) => Virtual;

		public Task<bool> RouteWidgetInteraction(string folderId, string widgetId, WidgetInteraction interaction)
		{
			Routed++;
			return Task.FromResult(true);
		}
	}

	private sealed class FakeActionExecutionCoordinator : IActionExecutionCoordinator, IDisposable
	{
		public FakeUiTransport Transport { get; } = new();

		public FlowExecutionResult? InlineResult { get; set; }

		public bool Slow { get; set; }

		public int Runs { get; private set; }

		public Action? OnRun { get; set; }

		public FlowExecutionRequest? LastRequest { get; private set; }

		public void Dispose() => Transport.Dispose();

		public Task<ActionExecutionDispatch> RunBoundedAsync(
			FlowExecutionRequest request,
			TimeSpan bound,
			CancellationToken cancellationToken)
		{
			Runs++;
			LastRequest = request;
			OnRun?.Invoke();
			var result = InlineResult ??
				new FlowExecutionResult
				{
					ExecutionId = request.ExecutionId,
					Status = FlowExecutionStatus.Succeeded,
					MatchedFlows = 1
				};

			if (!Slow)
			{
				return Task.FromResult(new ActionExecutionDispatch(request.ExecutionId, result));
			}

			if (request.OriginClientId is { } clientId)
			{
				var statusEvent = ActionExecutionDtoMapper.ToStatusEvent(result,
					request.OwnerWidgetId?.ToString(),
					request.Trigger.Value);
				_ = Transport.SendToGroup(UiClientGroups.For(clientId), statusEvent, cancellationToken);
			}

			return Task.FromResult(new ActionExecutionDispatch(request.ExecutionId, null));
		}
	}

	private sealed class FakeUiTransport : IUiTransport, IDisposable
	{
		public Task SendToConnection<T>(string connectionId, T message, CancellationToken cancellationToken = default)
			where T : class
			=> Task.CompletedTask;

		public Task AddToGroup(string connectionId, string group, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;

		public Task RemoveFromGroup(string connectionId, string group, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;

		private readonly SemaphoreSlim _signal = new(0);

		public List<(string Group, object Message)> Sent { get; } = [];

		public void Dispose() => _signal.Dispose();

		public Task Send<T>(T message, CancellationToken cancellationToken = default)
			where T : class
			=> Task.CompletedTask;

		public Task SendToGroup<T>(string group, T message, CancellationToken cancellationToken = default)
			where T : class
		{
			lock (Sent)
			{
				Sent.Add((group, message));
			}

			_signal.Release();
			return Task.CompletedTask;
		}

		public async Task WaitForSendAsync(int timeoutMs = 2000)
		{
			var acquired = await _signal.WaitAsync(timeoutMs);
			if (!acquired)
			{
				Assert.Fail("Expected a SendToGroup call that never arrived.");
			}
		}
	}

	private sealed class NoOpActionButtonStateService : IActionButtonStateService
	{
		public Task<WidgetStateWriteResult> SetAsync(Guid widgetId,
			string stateId,
			CancellationToken cancellationToken = default)
			=> Task.FromResult(WidgetStateWriteResult.Failed(WidgetStateWriteError.NotFound));

		public Task<WidgetStateWriteResult> AdvanceAsync(Guid widgetId, CancellationToken cancellationToken = default)
			=> Task.FromResult(WidgetStateWriteResult.Failed(WidgetStateWriteError.NotFound));
	}

	private sealed class RecordingWidgetService : IWidgetService
	{
		public WidgetEntity? Updated { get; private set; }

		public Task<Result<WidgetEntity, WidgetError>> Update(WidgetEntity widget)
		{
			Updated = widget;
			return Task.FromResult(Result.Ok<WidgetEntity, WidgetError>(widget));
		}

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

	private sealed class RecordingVariableService : IVariableService
	{
		public List<(string Name, object? Value)> Upserts { get; } = [];

		public Task UpsertWidgetVariable(VariableScope scope,
			string scopeRefId,
			string name,
			VariableType type,
			object? value)
		{
			Upserts.Add((name, value));
			return Task.CompletedTask;
		}

		public Task RemoveWidgetVariable(VariableScope scope, string scopeRefId, string name) => Task.CompletedTask;

		public Task<IReadOnlyList<VariableEntity>> GetAll() => throw new NotSupportedException();

		public Task<IReadOnlyList<VariableEntity>> GetByScope(VariableScope scope, string? scopeRefId)
			=> throw new NotSupportedException();

		public Task<VariableEntity?> GetById(Guid id) => throw new NotSupportedException();

		public Task<VariableEntity?> Resolve(string name, VariableScope contextScope, string? contextScopeRefId)
			=> throw new NotSupportedException();

		public Task<Result<VariableEntity, VariableError>> CreateUserVariable(
			string name,
			VariableScope scope,
			string? scopeRefId,
			VariableType type,
			object? initialValue,
			int? decimalPlaces) => throw new NotSupportedException();

		public Task<Result<VariableEntity, VariableError>> SetValue(Guid id,
			object? value,
			CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task<Result<VariableEntity, VariableError>> UpdateUserVariable(
			Guid id,
			string? name,
			int? decimalPlaces) => throw new NotSupportedException();

		public Task<Result<VariableError>> DeleteUserVariable(Guid id) => throw new NotSupportedException();

		public Task<Result<VariableEntity, VariableError>> CreateIntegrationVariable(
			string integrationId,
			string name,
			VariableScope scope,
			string? scopeRefId,
			VariableType type,
			object? initialValue,
			int? decimalPlaces,
			string? definitionId = null,
			VariableDeclaration? declaration = null,
			VariableUpdateMode updateMode = VariableUpdateMode.Polled) => throw new NotSupportedException();

		public Task<Result<VariableEntity, VariableError>> MaterializeCatalogVariable(string integrationId,
			string resourceId,
			string name,
			VariableType type,
			int? decimalPlaces,
			VariableDeclaration? declaration = null) =>
			throw new NotSupportedException();

		public Task<VariableEntity?> GetByDefinition(MacroDeck.Sdk.Identity.QualifiedId definitionId)
			=> Task.FromResult<VariableEntity?>(null);

		public Task<Result<VariableEntity, VariableError>> ReportIntegrationVariableValue(
			string integrationId,
			Guid id,
			object? value,
			VariableBounds? bounds = null) => throw new NotSupportedException();

		public Task<Result<VariableError>> SetIntegrationVariableAvailability(
			string integrationId,
			Guid id,
			bool available) => throw new NotSupportedException();

		public Task<Result<VariableError>> DeleteIntegrationVariable(string integrationId, Guid id)
			=> throw new NotSupportedException();

		public Task<IReadOnlyList<VariableEntity>> GetByOwnerIntegration(string integrationId)
			=> throw new NotSupportedException();

		public Task DeleteByScopeInstance(VariableScope scope, string scopeRefId) => throw new NotSupportedException();
	}

	private sealed class NoOpFlowExecutor : IFlowExecutor
	{
		public Task<FlowExecutionResult> ExecuteAsync(FlowExecutionRequest request, CancellationToken cancellationToken)
			=> Task.FromResult(new FlowExecutionResult
			{
				ExecutionId = request.ExecutionId,
				Status = FlowExecutionStatus.Succeeded,
				MatchedFlows = 0
			});
	}
}
