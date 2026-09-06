using System.Text.Json.Nodes;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Tests.UnitTests.Delegation;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Tests.UnitTests.Rendering;

/// <summary>
/// <see cref="IWidgetStateService.Resolve" /> replaced the old boolean <c>ResolveToggled</c> - it
/// returns a <see cref="WidgetStateResolution" /> naming a state id, not a bound-condition flag.
/// Scenarios B1-B7: first-match-wins mapping precedence, that reordering the same rules changes the
/// outcome, a rule naming a deleted state being skipped, the provider's own declared "unavailable"
/// state rendering as real, and B7 - the most important test in this whole change - a provider that
/// cannot answer holding its last known state and never falling through to a stashed mapping.
/// </summary>
[TestFixture]
public class WidgetStateServiceTests
{
	private static readonly string[] _offlineAndOn = ["offline", "on"];

	private static WidgetEntity Button(Guid id, string? data) => new()
	{
		Id = id,
		FolderId = Guid.NewGuid(),
		Type = WidgetTypeIds.ActionButton,
		Data = data
	};

	private static VariableEntity Cpu(string value) => new()
	{
		Id = Guid.NewGuid(),
		Name = "cpu",
		Scope = VariableScope.Global,
		Type = VariableType.Numeric,
		Classification = VariableClassification.User,
		Value = value
	};

	private sealed class Fixture
	{
		public Fixture(WidgetEntity widget, WidgetDerivedStateStore? derivedStore = null, params VariableEntity[] vars)
			: this(widget, derivedStore, optimisticStates: null, vars)
		{
		}

		public Fixture(WidgetEntity widget,
			WidgetOptimisticStateStore optimisticStates,
			WidgetDerivedStateStore? derivedStore = null,
			params VariableEntity[] vars)
			: this(widget, derivedStore, optimisticStates, vars)
		{
		}

		private Fixture(WidgetEntity widget,
			WidgetDerivedStateStore? derivedStore,
			WidgetOptimisticStateStore? optimisticStates,
			IReadOnlyList<VariableEntity> vars)
		{
			var registry = new VariableRegistry();
			foreach (var v in vars)
			{
				registry.Upsert(v);
			}

			var renderer = new VariableTemplateRenderer(registry);
			var evaluator = new ActionConditionEvaluator(renderer);
			var readiness = new StartupReadiness();
			readiness.MarkCachesReady();
			readiness.MarkVariablesReady();

			Integrations = new FakeIntegrationRegistry();
			Service = new WidgetStateService(new FakeFolderCache(widget),
				renderer,
				evaluator,
				Integrations,
				derivedStore ?? new WidgetDerivedStateStore(),
				readiness,
				optimisticStates);
		}

		public WidgetStateService Service { get; }
		public FakeIntegrationRegistry Integrations { get; }
	}

	[Test]
	public async Task NewActionButton_StartsWithStateModeDisabledAndNoStates()
	{
		var widget = Button(Guid.NewGuid(), "{}");
		var fixture = new Fixture(widget);

		Assert.That(await fixture.Service.Resolve(widget.Id), Is.Null);
	}

	[Test]
	public async Task MappingRules_FirstMatchWins_AndReorderingChangesTheOutcome()
	{
		const string ruleCritFirst =
			"{\"stateMode\":true,\"states\":[{\"id\":\"warn\",\"label\":\"Warn\"},{\"id\":\"crit\",\"label\":\"Crit\"}," +
			"{\"id\":\"ok\",\"label\":\"OK\"}],\"stateMapping\":{\"rules\":[" +
			"{\"id\":\"r1\",\"stateId\":\"crit\",\"when\":{\"kind\":\"compare\",\"left\":{\"$var\":\"cpu\"},\"operator\":\">=\",\"right\":90}}," +
			"{\"id\":\"r2\",\"stateId\":\"warn\",\"when\":{\"kind\":\"compare\",\"left\":{\"$var\":\"cpu\"},\"operator\":\">=\",\"right\":50}}]," +
			"\"fallbackStateId\":\"ok\"}}";
		const string ruleWarnFirst =
			"{\"stateMode\":true,\"states\":[{\"id\":\"warn\",\"label\":\"Warn\"},{\"id\":\"crit\",\"label\":\"Crit\"}," +
			"{\"id\":\"ok\",\"label\":\"OK\"}],\"stateMapping\":{\"rules\":[" +
			"{\"id\":\"r2\",\"stateId\":\"warn\",\"when\":{\"kind\":\"compare\",\"left\":{\"$var\":\"cpu\"},\"operator\":\">=\",\"right\":50}}," +
			"{\"id\":\"r1\",\"stateId\":\"crit\",\"when\":{\"kind\":\"compare\",\"left\":{\"$var\":\"cpu\"},\"operator\":\">=\",\"right\":90}}]," +
			"\"fallbackStateId\":\"ok\"}}";

		var critFirst = Button(Guid.NewGuid(), ruleCritFirst);
		var warnFirst = Button(Guid.NewGuid(), ruleWarnFirst);
		var cpu = Cpu("95");

		var critResult = await new Fixture(critFirst, vars: cpu).Service.Resolve(critFirst.Id);
		var warnResult = await new Fixture(warnFirst, vars: cpu).Service.Resolve(warnFirst.Id);

		Assert.Multiple(() =>
		{
			Assert.That(critResult?.StateId, Is.EqualTo("crit"));
			Assert.That(warnResult?.StateId,
				Is.EqualTo("warn"),
				"order-insensitive evaluation would resolve both to the same state");
		});
	}

	[Test]
	public async Task MappingWithNoMatchingRule_ResolvesToTheFallbackState()
	{
		const string data =
			"{\"stateMode\":true,\"states\":[{\"id\":\"warn\",\"label\":\"Warn\"},{\"id\":\"crit\",\"label\":\"Crit\"}," +
			"{\"id\":\"ok\",\"label\":\"OK\"}],\"stateMapping\":{\"rules\":[" +
			"{\"id\":\"r1\",\"stateId\":\"crit\",\"when\":{\"kind\":\"compare\",\"left\":{\"$var\":\"cpu\"},\"operator\":\">=\",\"right\":90}}," +
			"{\"id\":\"r2\",\"stateId\":\"warn\",\"when\":{\"kind\":\"compare\",\"left\":{\"$var\":\"cpu\"},\"operator\":\">=\",\"right\":50}}]," +
			"\"fallbackStateId\":\"ok\"}}";
		var widget = Button(Guid.NewGuid(), data);
		var fixture = new Fixture(widget, vars: Cpu("10"));

		var result = await fixture.Service.Resolve(widget.Id);

		Assert.That(result?.StateId, Is.EqualTo("ok"), "must fall back to the declared fallback, not states[0]");
	}

	[Test]
	public async Task MappingRuleTargetingADeletedState_NeverResolvesToADanglingStateId()
	{
		const string data =
			"{\"stateMode\":true,\"states\":[{\"id\":\"crit\",\"label\":\"Crit\"},{\"id\":\"ok\",\"label\":\"OK\"}]," +
			"\"stateMapping\":{\"rules\":[" +
			"{\"id\":\"r1\",\"stateId\":\"warn\",\"when\":{\"kind\":\"compare\",\"left\":{\"$var\":\"cpu\"},\"operator\":\">=\",\"right\":90}}," +
			"{\"id\":\"r2\",\"stateId\":\"crit\",\"when\":{\"kind\":\"compare\",\"left\":{\"$var\":\"cpu\"},\"operator\":\">=\",\"right\":50}}]," +
			"\"fallbackStateId\":\"ok\"}}";
		var widget = Button(Guid.NewGuid(), data);
		var fixture = new Fixture(widget, vars: Cpu("95"));

		WidgetStateResolution? result = null;
		Assert.DoesNotThrowAsync(async () => result = await fixture.Service.Resolve(widget.Id));

		Assert.Multiple(() =>
		{
			Assert.That(result?.StateId, Is.EqualTo("crit"), "a rule naming a deleted state is skipped, not fatal");
			Assert.That(result!.States.Select(s => s.Id), Does.Contain(result.StateId));
		});
	}

	// Regression for issue #718: a rules-less stateMapping left over from adding then removing a mapping
	// used to be treated as authoritative and win over the explicit activeStateId, answering only its own
	// fallback forever. It must not be consulted at all - activeStateId governs, without the widget ever
	// being re-saved (Resolve reads the raw stored data; nothing here calls Normalize).
	[Test]
	public async Task DegenerateMappingWithNoRules_NeverOverridesTheExplicitActiveState()
	{
		const string data =
			"{\"stateMode\":true,\"states\":[{\"id\":\"a\",\"label\":\"A\"},{\"id\":\"b\",\"label\":\"B\"}]," +
			"\"activeStateId\":\"b\",\"stateMapping\":{\"rules\":[],\"fallbackStateId\":\"a\"}}";
		var widget = Button(Guid.NewGuid(), data);
		var fixture = new Fixture(widget);

		var result = await fixture.Service.Resolve(widget.Id);

		Assert.That(result?.StateId, Is.EqualTo("b"), "the explicit active state, not the mapping's fallback");
	}

	// A8 - a state-mapping rule using a condition state operator reaches the same verdict a bare
	// ActionConditionEvaluator call would: isNotAvailable selects "unknown" when the referenced variable was
	// never declared, and the fallback "known" wins once it is.
	[Test]
	public async Task StateMapping_WithAStateOperatorRule_ReachesTheSameVerdictAsAConditionEvaluator()
	{
		const string data =
			"{\"stateMode\":true,\"states\":[{\"id\":\"unknown\",\"label\":\"Unknown\"},{\"id\":\"known\",\"label\":\"Known\"}]," +
			"\"stateMapping\":{\"rules\":[" +
			"{\"id\":\"m1\",\"stateId\":\"unknown\"," +
			"\"when\":{\"kind\":\"compare\",\"id\":\"m1\",\"left\":{\"$var\":\"artist\"},\"operator\":\"isNotAvailable\",\"right\":\"\"}}]," +
			"\"fallbackStateId\":\"known\"}}";

		var declaredWidget = Button(Guid.NewGuid(), data);
		var artist = new VariableEntity
		{
			Id = Guid.NewGuid(),
			Name = "artist",
			Scope = VariableScope.Global,
			Type = VariableType.Text,
			Classification = VariableClassification.User,
			Value = "Radiohead"
		};
		var declaredResult = await new Fixture(declaredWidget, vars: artist).Service.Resolve(declaredWidget.Id);

		var undeclaredWidget = Button(Guid.NewGuid(), data);
		var undeclaredResult = await new Fixture(undeclaredWidget).Service.Resolve(undeclaredWidget.Id);

		Assert.Multiple(() =>
		{
			Assert.That(declaredResult?.StateId, Is.EqualTo("known"));
			Assert.That(undeclaredResult?.StateId, Is.EqualTo("unknown"));
		});
	}

	[Test]
	public async Task ProviderReturningTheUnavailableState_RendersItAsARealState()
	{
		var action = new FakeStateProviderAction
		{
			Id = "provide",
			SnapshotToReturn = new ActionStateSnapshot(
				[new ActionStateDefinition("live", "Live"), new ActionStateDefinition("unavailable", "Unavailable")],
				"unavailable")
		};
		const string flows =
			"[{\"triggerId\":\"t\",\"triggerType\":\"onEvent\",\"children\":[{\"id\":\"blk-1\",\"type\":\"action\"," +
			"\"blockType\":\"integration.provide\",\"integrationId\":\"integration\",\"actionId\":\"provide\"," +
			"\"parameters\":[]}]}]";
		var data = "{\"stateMode\":true,\"states\":[{\"id\":\"live\",\"label\":\"Live\"}]," +
			"\"stateProvider\":{\"blockId\":\"blk-1\",\"integrationId\":\"integration\",\"actionId\":\"provide\"," +
			"\"states\":[{\"id\":\"live\",\"label\":\"Live\"}]}," +
			"\"flows\":" +
			flows +
			"}";
		var widget = Button(Guid.NewGuid(), data);
		var fixture = new Fixture(widget);
		fixture.Integrations.Add(new FakeIntegration { Id = "integration", Actions = [action] });

		var result = await fixture.Service.Resolve(widget.Id);

		Assert.Multiple(() =>
		{
			Assert.That(result?.StateId, Is.EqualTo("unavailable"));
			Assert.That(TestLocalization.Resolve(result?.StateLabel), Is.EqualTo("Unavailable"));
			Assert.That(result?.States.Select(s => s.Id), Does.Contain("unavailable"));
		});
	}

	[Test]
	public async Task ProviderReturningNullSnapshot_DoesNotFallBackToTheStashedMapping()
	{
		var action = new FakeStateProviderAction { Id = "provide", SnapshotToReturn = null };
		const string flows =
			"[{\"triggerId\":\"t\",\"triggerType\":\"onEvent\",\"children\":[{\"id\":\"blk-1\",\"type\":\"action\"," +
			"\"blockType\":\"integration.provide\",\"integrationId\":\"integration\",\"actionId\":\"provide\"," +
			"\"parameters\":[]}]}]";
		// A stashed mapping that WOULD match (cpu=95 -> "on" per its rule) sits alongside the provider,
		// exactly as provider adoption leaves it (issue #612 resolution 4). The provider is authoritative
		// unconditionally - even quiet - so this must never be consulted.
		var data = "{\"stateMode\":true,\"states\":[{\"id\":\"offline\",\"label\":\"Offline\"}," +
			"{\"id\":\"on\",\"label\":\"On\"}]," +
			"\"stateProvider\":{\"blockId\":\"blk-1\",\"integrationId\":\"integration\",\"actionId\":\"provide\"," +
			"\"states\":[{\"id\":\"offline\",\"label\":\"Offline\"},{\"id\":\"on\",\"label\":\"On\"}]}," +
			"\"manualStateBackup\":{\"states\":[{\"id\":\"offline\",\"label\":\"Offline\"},{\"id\":\"on\",\"label\":\"On\"}]," +
			"\"stateMapping\":{\"rules\":[{\"id\":\"r1\",\"stateId\":\"on\"," +
			"\"when\":{\"kind\":\"compare\",\"left\":{\"$var\":\"cpu\"},\"operator\":\">=\",\"right\":90}}]," +
			"\"fallbackStateId\":\"offline\"}}," +
			"\"flows\":" +
			flows +
			"}";
		var widget = Button(Guid.NewGuid(), data);
		var derivedStore = new WidgetDerivedStateStore();
		derivedStore.GetAndSet(widget.Id, "offline"); // as if a prior successful reconcile had run
		var fixture = new Fixture(widget, derivedStore, Cpu("95"));
		fixture.Integrations.Add(new FakeIntegration { Id = "integration", Actions = [action] });

		var result = await fixture.Service.Resolve(widget.Id);

		Assert.Multiple(() =>
		{
			Assert.That(result?.StateId,
				Is.EqualTo("offline"),
				"must hold the last known state, never the stashed mapping's match");
			Assert.That(result?.StateId, Is.Not.EqualTo("on"));
			Assert.That(result?.States.Select(s => s.Id),
				Is.EquivalentTo(_offlineAndOn),
				"the live states are still the provider's");
		});

		var rootAfter = (JsonObject)JsonNode.Parse(widget.Data!)!;
		Assert.That(rootAfter["stateMapping"],
			Is.Null,
			"the top-level mapping must stay absent - Resolve never restores it, even from the stash");
	}

	// The discriminating case: a top-level stateMapping sitting alongside the provider, resolving to a
	// DIFFERENT state than the held one. Hand-edited JSON and data predating the exclusivity rule can
	// both produce it. With the mapping only in the stash, an implementation that wrongly fell through
	// would land on ResolveExplicit and return the first state - the same answer - so that shape cannot
	// tell a correct implementation from a broken one.
	[Test]
	public async Task ProviderThatCannotAnswer_HoldsItsState_EvenWithATopLevelMappingThatWouldMatch()
	{
		var action = new FakeStateProviderAction { Id = "provide", SnapshotToReturn = null };
		const string flows =
			"[{\"triggerId\":\"t\",\"triggerType\":\"onEvent\",\"children\":[{\"id\":\"blk-1\",\"type\":\"action\"," +
			"\"blockType\":\"integration.provide\",\"integrationId\":\"integration\",\"actionId\":\"provide\"," +
			"\"parameters\":[]}]}]";
		var data = "{\"stateMode\":true,\"states\":[{\"id\":\"offline\",\"label\":\"Offline\"}," +
			"{\"id\":\"on\",\"label\":\"On\"}]," +
			"\"stateProvider\":{\"blockId\":\"blk-1\",\"integrationId\":\"integration\",\"actionId\":\"provide\"," +
			"\"states\":[{\"id\":\"offline\",\"label\":\"Offline\"},{\"id\":\"on\",\"label\":\"On\"}]}," +
			"\"stateMapping\":{\"rules\":[{\"id\":\"r1\",\"stateId\":\"on\"," +
			"\"when\":{\"kind\":\"compare\",\"left\":{\"$var\":\"cpu\"},\"operator\":\">=\",\"right\":90}}]," +
			"\"fallbackStateId\":\"on\"}," +
			"\"flows\":" +
			flows +
			"}";
		var widget = Button(Guid.NewGuid(), data);
		var derivedStore = new WidgetDerivedStateStore();
		derivedStore.GetAndSet(widget.Id, "offline");
		var fixture = new Fixture(widget, derivedStore, Cpu("95"));
		fixture.Integrations.Add(new FakeIntegration { Id = "integration", Actions = [action] });

		var result = await fixture.Service.Resolve(widget.Id);

		// Both the mapping's matching rule and its fallback name "on", so any fall-through - whether it
		// evaluated the rules or merely took the fallback - is distinguishable from holding "offline".
		Assert.That(result?.StateId,
			Is.EqualTo("offline"),
			"a provider that cannot answer holds its last known state; the mapping is never consulted");
	}

	[Test]
	public async Task OptimisticState_SuppressesStaleProviderStateUntilProviderConfirmsIt()
	{
		var action = ProviderAction("unmuted");
		var widget = ProviderWidget();
		var states = new WidgetOptimisticStateStore(TimeProvider.System);
		var identity = ProviderIdentity(widget.Id);
		states.TryApply(identity, states.Begin(identity), "muted");
		var fixture = new Fixture(widget, states);
		fixture.Integrations.Add(new FakeIntegration { Id = "integration", Actions = [action] });

		var whileStale = await fixture.Service.Resolve(widget.Id);
		action.SnapshotToReturn = ProviderSnapshot("muted");
		var confirmed = await fixture.Service.Resolve(widget.Id);
		action.SnapshotToReturn = ProviderSnapshot("unmuted");
		var afterConfirmation = await fixture.Service.Resolve(widget.Id);

		Assert.Multiple(() =>
		{
			Assert.That(whileStale?.StateId, Is.EqualTo("muted"));
			Assert.That(confirmed?.StateId, Is.EqualTo("muted"));
			Assert.That(afterConfirmation?.StateId,
				Is.EqualTo("unmuted"),
				"provider confirmation must clear the optimistic override");
		});
	}

	[Test]
	public async Task OptimisticState_ExpiresAfterExactlyFiveSeconds()
	{
		var time = new FakeTimeProvider();
		var states = new WidgetOptimisticStateStore(time);
		var widget = ProviderWidget();
		var identity = ProviderIdentity(widget.Id);
		states.TryApply(identity, states.Begin(identity), "muted");
		var fixture = new Fixture(widget, states);
		fixture.Integrations.Add(new FakeIntegration
		{
			Id = "integration", Actions = [ProviderAction("unmuted")]
		});

		time.Advance(TimeSpan.FromSeconds(5) - TimeSpan.FromTicks(1));
		var justBeforeTimeout = await fixture.Service.Resolve(widget.Id);
		time.Advance(TimeSpan.FromTicks(1));
		await Task.Yield();
		var atTimeout = await fixture.Service.Resolve(widget.Id);

		Assert.Multiple(() =>
		{
			Assert.That(justBeforeTimeout?.StateId, Is.EqualTo("muted"));
			Assert.That(atTimeout?.StateId, Is.EqualTo("unmuted"));
		});
	}

	[Test]
	public async Task ActionCompletingDuringProviderRead_WinsOverTheSnapshotAlreadyBeingRead()
	{
		var providerReply = new TaskCompletionSource<ActionStateSnapshot?>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		var action = new FakeStateProviderAction { Id = "provide", SnapshotTask = providerReply.Task };
		var widget = ProviderWidget();
		var states = new WidgetOptimisticStateStore(TimeProvider.System);
		var identity = ProviderIdentity(widget.Id);
		states.TryApply(identity, states.Begin(identity), "unmuted");
		var fixture = new Fixture(widget, states);
		fixture.Integrations.Add(new FakeIntegration { Id = "integration", Actions = [action] });

		var resolving = fixture.Service.Resolve(widget.Id);
		Assert.That(action.GetActionStateCallCount, Is.EqualTo(1));
		states.TryApply(identity, states.Begin(identity), "muted");
		providerReply.SetResult(ProviderSnapshot("unmuted"));

		var result = await resolving;

		Assert.That(result?.StateId, Is.EqualTo("muted"));
	}

	[Test]
	public async Task ProviderResolutionWithoutExpectation_IsInvalidatedByALaterExpectation()
	{
		var widget = ProviderWidget();
		var states = new WidgetOptimisticStateStore(TimeProvider.System);
		var identity = ProviderIdentity(widget.Id);
		var fixture = new Fixture(widget, states);
		fixture.Integrations.Add(new FakeIntegration
		{
			Id = "integration", Actions = [ProviderAction("unmuted")]
		});

		var providerResult = await fixture.Service.Resolve(widget.Id);
		states.TryApply(identity, states.Begin(identity), "muted");

		Assert.Multiple(() =>
		{
			Assert.That(providerResult?.OptimisticState, Is.Not.Null);
			Assert.That(states.IsCurrent(providerResult!.OptimisticState!), Is.False);
		});
	}

	[Test]
	public async Task ConfirmedExpectationResolution_IsInvalidatedByANewerExpectation()
	{
		var widget = ProviderWidget();
		var states = new WidgetOptimisticStateStore(TimeProvider.System);
		var identity = ProviderIdentity(widget.Id);
		states.TryApply(identity, states.Begin(identity), "muted");
		var fixture = new Fixture(widget, states);
		fixture.Integrations.Add(new FakeIntegration
		{
			Id = "integration", Actions = [ProviderAction("muted")]
		});

		var confirmedResult = await fixture.Service.Resolve(widget.Id);
		states.TryApply(identity, states.Begin(identity), "unmuted");

		Assert.Multiple(() =>
		{
			Assert.That(confirmedResult?.OptimisticState, Is.Not.Null);
			Assert.That(states.IsCurrent(confirmedResult!.OptimisticState!), Is.False);
		});
	}

	private static FakeStateProviderAction ProviderAction(string activeStateId) => new()
	{
		Id = "provide", SnapshotToReturn = ProviderSnapshot(activeStateId)
	};

	private static ActionStateSnapshot ProviderSnapshot(string activeStateId) => new(
		[new ActionStateDefinition("unmuted", "Unmuted"), new ActionStateDefinition("muted", "Muted")],
		activeStateId);

	private static WidgetOptimisticStateIdentity ProviderIdentity(Guid widgetId)
		=> new(widgetId, "blk-1", "integration", "provide");

	private static WidgetEntity ProviderWidget()
	{
		const string flows =
			"[{\"triggerId\":\"t\",\"triggerType\":\"onShortPress\",\"children\":[{\"id\":\"blk-1\",\"type\":\"action\"," +
			"\"blockType\":\"integration.provide\",\"integrationId\":\"integration\",\"actionId\":\"provide\"," +
			"\"parameters\":[]}]}]";
		var data = "{\"stateMode\":true,\"states\":[{\"id\":\"unmuted\",\"label\":\"Unmuted\"}," +
			"{\"id\":\"muted\",\"label\":\"Muted\"}]," +
			"\"stateProvider\":{\"blockId\":\"blk-1\",\"integrationId\":\"integration\",\"actionId\":\"provide\"," +
			"\"states\":[{\"id\":\"unmuted\",\"label\":\"Unmuted\"},{\"id\":\"muted\",\"label\":\"Muted\"}]}," +
			"\"flows\":" +
			flows +
			"}";
		return Button(Guid.NewGuid(), data);
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
