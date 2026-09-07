using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.Rendering;

[TestFixture]
public class WidgetStateReconcilerTests
{
	private static WidgetEntity BoundButton(Guid id) => new()
	{
		Id = id,
		FolderId = Guid.NewGuid(),
		Type = WidgetTypeIds.ActionButton,
		Data = "{\"stateMode\":true,\"states\":[{\"id\":\"off\",\"label\":\"Off\"},{\"id\":\"on\",\"label\":\"On\"}]," +
			"\"stateMapping\":{\"rules\":[{\"id\":\"r\",\"stateId\":\"on\",\"when\":{\"kind\":\"compare\"," +
			"\"left\":{\"$var\":\"cpu\"},\"operator\":\">=\",\"right\":80}}],\"fallbackStateId\":\"off\"}}"
	};

	private static (WidgetStateReconciler Reconciler, RecordingFlowExecutor Flow, RecordingVariableService Vars,
		WidgetDerivedStateStore Store, RecordingStatePublisher Publisher) Build(WidgetEntity widget,
		params string?[] resolvedStateIds)
	{
		var store = new WidgetDerivedStateStore();
		var flow = new RecordingFlowExecutor();
		var vars = new RecordingVariableService();
		var publisher = new RecordingStatePublisher();
		var reconciler = new WidgetStateReconciler(new QueuedStateService(resolvedStateIds),
			store,
			vars,
			flow,
			publisher,
			new FakeFolderCache(widget),
			new NotSupportedWidgetService(),
			new WidgetDataWriteLock(),
			TestLocalization.Preferences,
			TestLocalization.Resolver,
			Serilog.Log.Logger);
		return (reconciler, flow, vars, store, publisher);
	}

	[Test]
	public async Task FirstEvaluation_seeds_var_without_firing_flow()
	{
		var widget = BoundButton(Guid.NewGuid());
		var (reconciler, flow, vars, _, _) = Build(widget, "on");

		var result = await reconciler.Reconcile(widget.Id);

		Assert.Multiple(() =>
		{
			Assert.That(result.StateId, Is.EqualTo("on"));
			Assert.That(result.Changed, Is.True, "first value seen counts as changed for the display push");
			Assert.That(result.Transitioned, Is.False, "no prior state means no state-change flow");
			Assert.That(flow.Triggers, Is.Empty);
			Assert.That(vars.Upserts, Has.Count.EqualTo(2), "vars.state and vars.stateLabel are both seeded");
			Assert.That(vars.Upserts,
				Has.Some.Matches<(string Name, object? Value)>(u => u.Name == "state" && Equals(u.Value, "on")));
			Assert.That(vars.Upserts,
				Has.Some.Matches<(string Name, object? Value)>(u => u.Name == "state_label" && Equals(u.Value, "On")));
		});
	}

	[Test]
	public async Task Transition_fires_onStateChange_and_updates_var()
	{
		var widget = BoundButton(Guid.NewGuid());
		var (reconciler, flow, vars, _, _) = Build(widget, "off", "on");

		await reconciler.Reconcile(widget.Id); // seed = off
		var result = await reconciler.Reconcile(widget.Id); // flip to on

		Assert.Multiple(() =>
		{
			Assert.That(result.Transitioned, Is.True);
			Assert.That(result.Changed, Is.True);
			Assert.That(flow.Triggers, Has.Count.EqualTo(1));
			Assert.That(flow.Triggers[0], Is.EqualTo("onStateChange"));
			Assert.That(vars.Upserts[^2], Is.EqualTo(("state", (object?)"on")));
			Assert.That(vars.Upserts[^1], Is.EqualTo(("state_label", (object?)"On")));
		});
	}

	[Test]
	public async Task Transition_marks_its_flow_host_originated_so_it_is_not_gated_while_locked()
	{
		var widget = BoundButton(Guid.NewGuid());
		var (reconciler, flow, _, _, _) = Build(widget, "off", "on");

		await reconciler.Reconcile(widget.Id); // seed = off
		await reconciler.Reconcile(widget.Id); // flip to on

		Assert.That(flow.Requests.Single().Origin, Is.EqualTo(ExecutionOrigin.Host));
	}

	[Test]
	public async Task UnchangedValue_does_not_fire_flow_or_report_changed()
	{
		var widget = BoundButton(Guid.NewGuid());
		var (reconciler, flow, _, _, _) = Build(widget, "on", "on");

		await reconciler.Reconcile(widget.Id); // seed = on
		var result = await reconciler.Reconcile(widget.Id); // still on

		Assert.Multiple(() =>
		{
			Assert.That(result.Changed, Is.False);
			Assert.That(result.Transitioned, Is.False);
			Assert.That(flow.Triggers, Is.Empty);
		});
	}

	[Test]
	public async Task Unresolvable_widget_returns_none_and_forgets_state()
	{
		var widget = BoundButton(Guid.NewGuid());
		var (reconciler, flow, _, store, _) = Build(widget, "on", null);

		await reconciler.Reconcile(widget.Id); // seed = on
		var result = await reconciler.Reconcile(widget.Id); // now unbound / unresolvable

		Assert.Multiple(() =>
		{
			Assert.That(result.StateId, Is.Null);
			Assert.That(result.Changed, Is.False);
			Assert.That(flow.Triggers, Is.Empty);
			// Re-binding later must not see a stale prior value and spuriously fire.
			Assert.That(store.GetAndSet(widget.Id, "on"), Is.Null);
		});
	}

	[TestCase(true)]
	[TestCase(false)]
	public async Task NewerOptimisticGenerationBeforeCommit_DiscardsAndReResolvesTheStaleResolution(
		bool installInitialExpectation)
	{
		var widget = BoundButton(Guid.NewGuid());
		var identity = new WidgetOptimisticStateIdentity(widget.Id, "provider", "integration", "toggle");
		var optimisticStates = new WidgetOptimisticStateStore(TimeProvider.System);
		if (installInitialExpectation)
		{
			var firstGeneration = optimisticStates.Begin(identity);
			optimisticStates.TryApply(identity, firstGeneration, "off");
		}

		var stateService = new MutableOptimisticStateService(optimisticStates,
			identity,
			optimisticStates.Observe(identity));
		var vars = new RecordingVariableService
		{
			OnFirstUpsert = () => optimisticStates.TryApply(identity, optimisticStates.Begin(identity), "on")
		};
		var derivedStates = new WidgetDerivedStateStore();
		var flow = new RecordingFlowExecutor();
		var reconciler = new WidgetStateReconciler(stateService,
			derivedStates,
			vars,
			flow,
			new NoOpWidgetStatePublisher(),
			new FakeFolderCache(widget),
			new NotSupportedWidgetService(),
			new WidgetDataWriteLock(),
			TestLocalization.Preferences,
			TestLocalization.Resolver,
			Serilog.Log.Logger,
			optimisticStates);

		var result = await reconciler.Reconcile(widget.Id);

		Assert.Multiple(() =>
		{
			Assert.That(result.StateId, Is.EqualTo("on"));
			Assert.That(stateService.ResolveCount, Is.EqualTo(2));
			Assert.That(derivedStates.GetAndSet(widget.Id, "on"), Is.EqualTo("on"));
			Assert.That(flow.Triggers, Is.Empty);
		});
	}

	// Scenario E4: driving the same mapping through cpu = 50 -> 95 -> 96 -> 40 must fire onStateChange
	// exactly twice - not on the seed (50), and not on the no-op re-evaluation at 96 that stays "on".
	[Test]
	public async Task StateTransition_FiresOnStateChangeOncePerActualTransition()
	{
		var widget = BoundButton(Guid.NewGuid());
		var (reconciler, flow, _, _, _) = Build(widget, "off", "on", "on", "off"); // cpu: 50, 95, 96, 40

		foreach (var _ in Enumerable.Range(0, 4))
		{
			await reconciler.Reconcile(widget.Id);
		}

		Assert.That(flow.Triggers, Has.Count.EqualTo(2), "not on the seed, not on the unchanged 96 tick");
		Assert.That(flow.Triggers, Is.All.EqualTo("onStateChange"));
	}

	// Scenario B8: a rule whose condition reads the widget's own vars.state - a realistic "go active
	// exactly once, then stay there" pattern - must settle to a fixed point rather than oscillate.
	// Five repeated ticks (as WidgetStateEvalBackgroundService would drive after each vars.state write
	// re-enqueues the widget) fire onStateChange at most once and never hang.
	[Test]
	public async Task MappingConditionReferencingVarsState_SettlesInsteadOfOscillating()
	{
		const string data =
			"{\"stateMode\":true,\"states\":[{\"id\":\"pending\",\"label\":\"Pending\"},{\"id\":\"active\",\"label\":\"Active\"}]," +
			"\"stateMapping\":{\"rules\":[{\"id\":\"r\",\"stateId\":\"active\"," +
			"\"when\":{\"kind\":\"compare\",\"left\":{\"$var\":\"state\"},\"operator\":\"!=\",\"right\":\"\"}}]," +
			"\"fallbackStateId\":\"pending\"}}";
		var widget = new WidgetEntity
		{
			Id = Guid.NewGuid(), FolderId = Guid.NewGuid(), Type = WidgetTypeIds.ActionButton, Data = data
		};

		var folderCache = new FakeFolderCache(widget);
		var registry = new VariableRegistry();
		var renderer = new VariableTemplateRenderer(registry);
		var evaluator = new ActionConditionEvaluator(renderer);
		var readiness = new StartupReadiness();
		readiness.MarkCachesReady();
		readiness.MarkVariablesReady();

		var services = new ServiceCollection();
		services.AddSingleton(registry);
		services.AddSingleton<Mediator.IMediator, RecordingMediator>();
		services.AddSingleton<IUserVariableStore, NullUserVariableStore>();
		services.AddTestVariableService();
		await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
		using var scope = provider.CreateScope();
		var variableService = scope.ServiceProvider.GetRequiredService<IVariableService>();

		var stateService = new WidgetStateService(folderCache,
			renderer,
			evaluator,
			new FakeIntegrationRegistry(),
			new WidgetDerivedStateStore(),
			readiness);
		var flow = new RecordingFlowExecutor();
		var reconciler = new WidgetStateReconciler(stateService,
			new WidgetDerivedStateStore(),
			variableService,
			flow,
			new NoOpWidgetStatePublisher(),
			folderCache,
			new NotSupportedWidgetService(),
			new WidgetDataWriteLock(),
			TestLocalization.Preferences,
			TestLocalization.Resolver,
			Serilog.Log.Logger);

		var ticks = Task.Run(async () =>
		{
			for (var i = 0; i < 5; i++)
			{
				await reconciler.Reconcile(widget.Id);
			}
		});
		var finished = await Task.WhenAny(ticks, Task.Delay(TimeSpan.FromSeconds(5)));

		Assert.That(finished, Is.SameAs(ticks), "the reconciler hung instead of settling");
		Assert.That(flow.Triggers.Count, Is.LessThanOrEqualTo(1), "at most one onStateChange across 5 ticks");
	}

	// Issue #678: a client that is told about the transition only once the flow has finished cannot see
	// anything the flow paints while it runs - a Set Border it also turns off again is invisible.
	[Test]
	public async Task Transition_IsPublishedBeforeItsOnStateChangeFlowRuns()
	{
		var widget = BoundButton(Guid.NewGuid());
		var (reconciler, flow, _, _, publisher) = Build(widget, "off", "on");
		var publishedWhenFlowStarted = -1;
		flow.OnExecuting = () =>
		{
			publishedWhenFlowStarted = publisher.Published.Count;
			return Task.CompletedTask;
		};

		await reconciler.Reconcile(widget.Id); // seed = off
		await reconciler.Reconcile(widget.Id); // flip to on

		Assert.That(publishedWhenFlowStarted, Is.EqualTo(2), "the transition was still unpublished when its flow ran");
		Assert.That(publisher.Published[^1].StateId, Is.EqualTo("on"));
	}

	[Test]
	public async Task Reconcile_DoesNotReturnUntilItsOnStateChangeFlowHasFinished()
	{
		var widget = BoundButton(Guid.NewGuid());
		var (reconciler, flow, _, _, _) = Build(widget, "off", "on");
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		flow.OnExecuting = () => release.Task;

		await reconciler.Reconcile(widget.Id); // seed = off
		var reconcile = reconciler.Reconcile(widget.Id);

		Assert.That(await Task.WhenAny(reconcile, Task.Delay(200)), Is.Not.SameAs(reconcile),
			"Set/Cycle Button State rely on the flow having run before the call returns");
		release.SetResult();
		Assert.That((await reconcile).Transitioned, Is.True);
	}

	// The seed is a change without a transition, and it is the one the Changed gate exists for: it must
	// still be published even though it fires no flow.
	[Test]
	public async Task Seed_IsPublishedAndFiresNoFlow()
	{
		var widget = BoundButton(Guid.NewGuid());
		var (reconciler, flow, _, _, publisher) = Build(widget, "on");

		await reconciler.Reconcile(widget.Id);

		Assert.Multiple(() =>
		{
			Assert.That(publisher.Published.Single().Changed, Is.True);
			Assert.That(publisher.Published.Single().Transitioned, Is.False);
			Assert.That(flow.Triggers, Is.Empty);
		});
	}

	// The push travels an unisolated signal multicast and a real socket write. Neither is allowed to
	// stop the flow: an unreachable client must not cancel host business logic.
	[Test]
	public async Task PublishFailure_StillLeavesTheOnStateChangeFlowFired()
	{
		var widget = BoundButton(Guid.NewGuid());
		var (reconciler, flow, _, _, publisher) = Build(widget, "off", "on");
		publisher.OnPublishing = () => throw new InvalidOperationException("a session handler threw");

		await reconciler.Reconcile(widget.Id); // seed = off
		var result = await reconciler.Reconcile(widget.Id); // flip to on

		Assert.Multiple(() =>
		{
			Assert.That(flow.Triggers, Is.EqualTo(new[] { "onStateChange" }));
			Assert.That(result.StateId, Is.EqualTo("on"));
		});
	}

	private sealed class RecordingStatePublisher : IWidgetStatePublisher
	{
		private readonly List<WidgetStateReconciliation> _published = [];

		public List<WidgetStateReconciliation> Published
		{
			get
			{
				lock (_published)
				{
					return _published.ToList();
				}
			}
		}

		public Func<Task>? OnPublishing { get; set; }

		public async Task PublishIfChanged(Guid widgetId,
			WidgetStateReconciliation result,
			CancellationToken cancellationToken = default)
		{
			lock (_published)
			{
				_published.Add(result);
			}

			if (OnPublishing is { } callback)
			{
				await callback();
			}
		}
	}

	private sealed class QueuedStateService : IWidgetStateService
	{
		private readonly Queue<WidgetStateResolution?> _values;

		public QueuedStateService(params string?[] stateIds)
			=> _values = new Queue<WidgetStateResolution?>(stateIds.Select(ToResolution));

		public Task<WidgetStateResolution?> Resolve(Guid widgetId, CancellationToken cancellationToken = default)
			=> Task.FromResult(_values.Count > 0 ? _values.Dequeue() : null);

		public TimeSpan? GetProviderPollInterval(Guid widgetId) => null;

		private static WidgetStateResolution? ToResolution(string? stateId)
			=> stateId is null
				? null
				: new WidgetStateResolution(stateId, stateId == "on" ? "On" : "Off", [], false);
	}

	private sealed class MutableOptimisticStateService : IWidgetStateService
	{
		private readonly WidgetOptimisticStateStore _states;
		private readonly WidgetOptimisticStateIdentity _identity;
		private readonly WidgetOptimisticStateVersion _firstVersion;

		public MutableOptimisticStateService(
			WidgetOptimisticStateStore states,
			WidgetOptimisticStateIdentity identity,
			WidgetOptimisticStateVersion firstVersion)
		{
			_states = states;
			_identity = identity;
			_firstVersion = firstVersion;
		}

		public int ResolveCount { get; private set; }

		public Task<WidgetStateResolution?> Resolve(Guid widgetId, CancellationToken cancellationToken = default)
		{
			ResolveCount++;
			var version = ResolveCount == 1 ? _firstVersion : _states.Observe(_identity);
			var stateId = ResolveCount == 1 ? "off" : "on";
			return Task.FromResult<WidgetStateResolution?>(new WidgetStateResolution(stateId,
				stateId == "on" ? "On" : "Off",
				[],
				false)
			{
				OptimisticState = version
			});
		}

		public TimeSpan? GetProviderPollInterval(Guid widgetId) => null;
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

	private sealed class RecordingFlowExecutor : IFlowExecutor
	{
		public List<string> Triggers { get; } = [];

		public List<FlowExecutionRequest> Requests { get; } = [];

		public Func<Task>? OnExecuting { get; set; }

		public async Task<FlowExecutionResult> ExecuteAsync(FlowExecutionRequest request,
			CancellationToken cancellationToken)
		{
			Triggers.Add(request.Trigger.Value);
			Requests.Add(request);

			if (OnExecuting is { } callback)
			{
				await callback();
			}

			return new FlowExecutionResult
			{
				ExecutionId = Guid.NewGuid(),
				Status = FlowExecutionStatus.Succeeded,
				MatchedFlows = 1
			};
		}
	}

	private sealed class RecordingVariableService : IVariableService
	{
		public List<(string Name, object? Value)> Upserts { get; } = [];
		public Action? OnFirstUpsert { get; init; }

		public Task UpsertWidgetVariable(
			VariableScope scope,
			string scopeRefId,
			string name,
			VariableType type,
			object? value)
		{
			Upserts.Add((name, value));
			if (Upserts.Count == 1)
			{
				OnFirstUpsert?.Invoke();
			}

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

		public Task<VariableEntity?> GetByDefinition(QualifiedId definitionId) =>
			Task.FromResult<VariableEntity?>(null);

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

	private sealed class NullUserVariableStore : IUserVariableStore
	{
		public IReadOnlyList<VariableEntity> Load() => [];

		public void Save(IEnumerable<VariableEntity> userVariables)
		{
		}
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
}
