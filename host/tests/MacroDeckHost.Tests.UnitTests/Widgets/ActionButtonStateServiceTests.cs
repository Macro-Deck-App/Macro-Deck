using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.Widgets;

namespace MacroDeckHost.Tests.UnitTests.Widgets;

/// <summary>
/// Covers the explicit "Set Button State"/"Cycle Button State" write path (scenarios D1-D6), plus the
/// per-widget write-lock property carried over from <c>UpdateWidgetDataRuntimeGuardTests</c>: the write
/// must complete and reconcile (firing any onStateChange flow) without holding its own lock, or a flow
/// that targets the same button back deadlocks against itself. This was gotten wrong once already.
/// </summary>
[TestFixture]
public class ActionButtonStateServiceTests
{
	private const string TwoStates =
		"{\"stateMode\":true,\"states\":[{\"id\":\"off\",\"label\":\"Off\"},{\"id\":\"on\",\"label\":\"On\"}]," +
		"\"activeStateId\":\"off\"}";

	private const string ThreeStates =
		"{\"stateMode\":true,\"states\":[{\"id\":\"away\",\"label\":\"Away\"},{\"id\":\"off\",\"label\":\"Off\"}," +
		"{\"id\":\"on\",\"label\":\"On\"}],\"activeStateId\":\"away\"}";

	[Test]
	public async Task SetStateAction_SetsTheActiveStateWhenNoProviderOrMappingIsActive()
	{
		var fixture = new Fixture(TwoStates);

		var result = await fixture.Service.SetAsync(fixture.WidgetId, "on");

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(result.StateId, Is.EqualTo("on"));
			Assert.That(fixture.Widget.Data, Does.Contain("\"activeStateId\":\"on\""));
			Assert.That(fixture.Vars.Upserts,
				Has.Some.Matches<(string Name, object? Value)>(u => u.Name == "state" && Equals(u.Value, "on")));
			Assert.That(fixture.Vars.Upserts,
				Has.Some.Matches<(string Name, object? Value)>(u => u.Name == "state_label" && Equals(u.Value, "On")));
			Assert.That(fixture.Publisher.Calls, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task ToggleStateAction_AdvancesToTheNextStateAndWrapsPastTheLast()
	{
		var fixture = new Fixture(ThreeStates);

		var first = await fixture.Service.AdvanceAsync(fixture.WidgetId); // away -> off
		var second = await fixture.Service.AdvanceAsync(fixture.WidgetId); // off -> on
		var third = await fixture.Service.AdvanceAsync(fixture.WidgetId); // on -> away (wraps)

		Assert.Multiple(() =>
		{
			Assert.That(first.StateId, Is.EqualTo("off"));
			Assert.That(second.StateId, Is.EqualTo("on"));
			Assert.That(third.StateId, Is.EqualTo("away"), "a boolean flip never reaches the third state");
		});
	}

	[Test]
	public async Task SetStateAction_FailsWhenTheStateIsDeterminedByAProvider()
	{
		const string data =
			"{\"stateMode\":true,\"states\":[{\"id\":\"off\",\"label\":\"Off\"},{\"id\":\"on\",\"label\":\"On\"}]," +
			"\"activeStateId\":\"off\",\"stateProvider\":{\"blockId\":\"blk-1\",\"integrationId\":\"i\"," +
			"\"actionId\":\"a\",\"states\":[{\"id\":\"off\",\"label\":\"Off\"}]}}";
		var fixture = new Fixture(data);

		var result = await fixture.Service.SetAsync(fixture.WidgetId, "on");

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(WidgetStateWriteError.ProviderActive));
			Assert.That(fixture.Widgets.Updated, Is.Empty, "nothing written");
			Assert.That(fixture.Publisher.Calls, Is.EqualTo(0), "no push");
		});
	}

	[Test]
	public async Task ToggleStateAction_FailsWhenAStateMappingWithARealRuleIsActive()
	{
		// A rule-carrying mapping is authoritative, unlike the rules-less case covered below - it can
		// answer something activeStateId alone cannot, so it still locks out Set/Cycle State.
		const string data =
			"{\"stateMode\":true,\"states\":[{\"id\":\"off\",\"label\":\"Off\"},{\"id\":\"on\",\"label\":\"On\"}]," +
			"\"activeStateId\":\"off\",\"stateMapping\":{\"rules\":[{\"id\":\"r1\",\"stateId\":\"on\"," +
			"\"when\":{\"kind\":\"compare\",\"left\":{\"$var\":\"cpu\"},\"operator\":\">=\",\"right\":90}}]," +
			"\"fallbackStateId\":\"off\"}}";
		var fixture = new Fixture(data);

		var result = await fixture.Service.AdvanceAsync(fixture.WidgetId);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(WidgetStateWriteError.MappingActive));
			Assert.That(fixture.Widgets.Updated, Is.Empty);
			Assert.That(fixture.Publisher.Calls, Is.EqualTo(0));
		});
	}

	// Regression for issue #718: adding then removing a mapping used to leave a rules-less stateMapping
	// residue behind, which permanently refused Set/Cycle State even though nothing about the button's
	// own configuration (states, activeStateId) still depended on it. A mapping with no usable rule can
	// only ever answer its own fallback - exactly what activeStateId already says - so it must not be
	// treated as authoritative.
	[Test]
	public async Task ToggleStateAction_AdvancesWhenTheStoredMappingHasNoRules()
	{
		const string data =
			"{\"stateMode\":true,\"states\":[{\"id\":\"a\",\"label\":\"A\"},{\"id\":\"b\",\"label\":\"B\"}]," +
			"\"activeStateId\":\"a\",\"stateMapping\":{\"rules\":[],\"fallbackStateId\":\"a\"}}";
		var fixture = new Fixture(data);

		var result = await fixture.Service.AdvanceAsync(fixture.WidgetId);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(result.StateId, Is.EqualTo("b"));
		});
	}

	[Test]
	public async Task SetStateAction_FailsForAStateIdTheButtonDoesNotHave()
	{
		var fixture = new Fixture(TwoStates);

		var result = await fixture.Service.SetAsync(fixture.WidgetId, "unknown");

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(WidgetStateWriteError.UnknownState));
			Assert.That(fixture.Widgets.Updated, Is.Empty);
		});
	}

	[Test]
	public async Task SetStateAction_FailsWhenStateModeIsDisabled()
	{
		var fixture = new Fixture("{}");

		var result = await fixture.Service.SetAsync(fixture.WidgetId, "off");

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.Not.Null);
			Assert.That(fixture.Widget.Data,
				Does.Not.Contain("stateMode"),
				"must not implicitly enable State Mode");
			Assert.That(fixture.Widgets.Updated, Is.Empty);
		});
	}

	// Carried over from UpdateWidgetDataRuntimeGuardTests.AFlowThatWritesTheSameWidget_DoesNotDeadlockThePress:
	// the reconciler used to run while ActionButtonStateService still held the per-widget write lock, so
	// an onStateChange flow containing a widget action targeting this same button deadlocked against
	// itself. The write must complete, and reconcile, entirely off the lock.
	[Test]
	public async Task AnOnStateChangeFlowTargetingTheSameButton_DoesNotDeadlockTheWrite()
	{
		var fixture = new Fixture(TwoStates);

		// Seed the derived-state store so the next write is a real transition, not the initial seed
		// (which never fires onStateChange).
		var seed = await fixture.Service.SetAsync(fixture.WidgetId, "off");
		Assert.That(seed.Success, Is.True);

		fixture.Flow.OnFlowExecuting = () =>
		{
			fixture.Flow.OnFlowExecuting = null; // one-shot: simulate a single widget action, not a loop
			return fixture.Service.AdvanceAsync(fixture.WidgetId);
		};

		var write = fixture.Service.SetAsync(fixture.WidgetId, "on");
		var finished = await Task.WhenAny(write, Task.Delay(TimeSpan.FromSeconds(5)));

		Assert.That(finished, Is.SameAs(write), "the write deadlocked against its own lock");
		Assert.That((await write).Success, Is.True);
	}

	private sealed class Fixture
	{
		public Fixture(string data)
		{
			WidgetId = Guid.NewGuid();
			Widget = new WidgetEntity
			{
				Id = WidgetId,
				FolderId = Guid.NewGuid(),
				Type = WidgetTypeIds.ActionButton,
				Data = data
			};

			var folderCache = new SingleWidgetFolderCache(Widget);
			Widgets = new RecordingWidgetService();
			Vars = new RecordingVariableService();
			Publisher = new RecordingPublisher();
			Flow = new ReentrantFlowExecutor();
			var writeLock = new WidgetDataWriteLock();
			var registry = new VariableRegistry();
			var renderer = new VariableTemplateRenderer(registry);
			var evaluator = new ActionConditionEvaluator(renderer);
			var readiness = new StartupReadiness();
			readiness.MarkCachesReady();
			readiness.MarkVariablesReady();

			var stateService = new WidgetStateService(folderCache,
				renderer,
				evaluator,
				new FakeIntegrationRegistry(),
				new WidgetDerivedStateStore(),
				readiness);
			var reconciler = new WidgetStateReconciler(stateService,
				new WidgetDerivedStateStore(),
				Vars,
				Flow,
				folderCache,
				Widgets,
				writeLock,
				TestLocalization.Preferences,
				TestLocalization.Resolver,
				Serilog.Log.Logger);

			Service = new ActionButtonStateService(folderCache, writeLock, Widgets, reconciler, Publisher);
		}

		public Guid WidgetId { get; }
		public WidgetEntity Widget { get; }
		public RecordingWidgetService Widgets { get; }
		public RecordingVariableService Vars { get; }
		public RecordingPublisher Publisher { get; }
		public ReentrantFlowExecutor Flow { get; }
		public ActionButtonStateService Service { get; }
	}

	// A reconciler's derived-state store is created fresh per fixture above, so its own copy tracks
	// transitions independently of WidgetStateService's - both are seeded together since every write
	// runs through the same reconciler instance.
	private sealed class ReentrantFlowExecutor : IFlowExecutor
	{
		public Func<Task>? OnFlowExecuting { get; set; }

		public async Task<FlowExecutionResult> ExecuteAsync(FlowExecutionRequest request,
			CancellationToken cancellationToken)
		{
			if (OnFlowExecuting is { } callback)
			{
				await callback();
			}

			return new FlowExecutionResult
			{
				ExecutionId = request.ExecutionId,
				Status = FlowExecutionStatus.Succeeded,
				MatchedFlows = 1
			};
		}
	}

	private sealed class RecordingPublisher : IWidgetStatePublisher
	{
		public int Calls { get; private set; }

		public Task PublishIfChanged(Guid widgetId,
			WidgetStateReconciliation result,
			CancellationToken cancellationToken = default)
		{
			Calls++;
			return Task.CompletedTask;
		}
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

	private sealed class SingleWidgetFolderCache : IFolderCache
	{
		private readonly FolderEntity _folder;

		public SingleWidgetFolderCache(WidgetEntity widget)
			=> _folder = new FolderEntity { Id = widget.FolderId, Name = "f", Order = 0, Widgets = [widget] };

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

	private sealed class RecordingWidgetService : IWidgetService
	{
		public List<WidgetEntity> Updated { get; } = [];

		public Task<Result<WidgetEntity, WidgetError>> Update(WidgetEntity widget)
		{
			Updated.Add(widget);
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
}
