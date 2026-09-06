using System.Text.Json.Nodes;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Widgets;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Tests.UnitTests.Rendering;

/// <summary>
/// The guard chain a state-provider block must pass before it is ever read (<c>WidgetStateService.FindProviderAction</c>):
/// found by id - including nested inside a condition block's branches - enabled, its integration
/// registered and enabled, and its action a state provider. Any failure renders as "unavailable"
/// (scenario B6/B7's fallback), never a throw. Also covers C6-C9: single-provider-per-button, two
/// same-type instances distinguished by block id, parameters-only reads that never run the action's
/// side effect, and a misbehaving snapshot being ignored rather than applied.
/// </summary>
[TestFixture]
public class WidgetStateProviderResolutionTests
{
	private static readonly string[] _aThenB = ["a", "b"];


	private static JsonObject ProviderData(string blockId, IReadOnlyList<string> stateIds)
	{
		JsonArray StateArray() => new(stateIds.Select(id => (JsonNode)new JsonObject
		{
			["id"] = JsonValue.Create(id), ["label"] = JsonValue.Create(id)
		}).ToArray());

		return new JsonObject
		{
			["stateMode"] = JsonValue.Create(true),
			["states"] = StateArray(),
			["stateProvider"] = new JsonObject
			{
				["blockId"] = JsonValue.Create(blockId),
				["integrationId"] = JsonValue.Create("integration"),
				["actionId"] = JsonValue.Create("provide"),
				["states"] = StateArray()
			}
		};
	}

	private static WidgetEntity Button(Guid id, string flows, JsonObject providerData)
	{
		// The stateProvider's own data bag and the flows array live side by side in the same Data blob.
		var data = (JsonObject)providerData.DeepClone();
		data["flows"] = JsonNode.Parse(flows);
		return new WidgetEntity
			{ Id = id, FolderId = Guid.NewGuid(), Type = WidgetTypeIds.ActionButton, Data = data.ToJsonString() };
	}

	private static (WidgetStateService Service, FakeIntegrationRegistry Registry) Build(WidgetEntity widget)
	{
		var readiness = new StartupReadiness();
		readiness.MarkCachesReady();
		readiness.MarkVariablesReady();
		var registry = new FakeIntegrationRegistry();
		var renderer = new VariableTemplateRenderer(new VariableRegistry());
		var service = new WidgetStateService(new SingleWidgetFolderCache(widget),
			renderer,
			new ActionConditionEvaluator(renderer),
			registry,
			new WidgetDerivedStateStore(),
			readiness);
		return (service, registry);
	}

	private static string FlowWithBlock(string blockId, bool disabled = false, string? blockType = null)
		=> "[{\"triggerId\":\"t\",\"triggerType\":\"onEvent\",\"children\":[{\"id\":\"" +
			blockId +
			"\"," +
			"\"type\":\"action\",\"blockType\":\"" +
			(blockType ?? "integration.provide") +
			"\"," +
			"\"integrationId\":\"integration\",\"actionId\":\"provide\",\"disabled\":" +
			(disabled ? "true" : "false") +
			",\"parameters\":[]}]}]";

	[Test]
	public async Task BlockFoundById_InsideNestedChildrenAndBranches()
	{
		var action = new FakeStateProviderAction
		{
			Id = "provide", SnapshotToReturn = new ActionStateSnapshot([new ActionStateDefinition("a", "A")], "a")
		};

		const string flows =
			"[{\"triggerId\":\"t\",\"triggerType\":\"onEvent\",\"children\":[" +
			"{\"id\":\"outer\",\"type\":\"condition\",\"children\":[]," +
			"\"branches\":[{\"id\":\"br-1\",\"kind\":\"if\",\"children\":[" +
			"{\"id\":\"blk-1\",\"type\":\"action\",\"blockType\":\"integration.provide\"," +
			"\"integrationId\":\"integration\",\"actionId\":\"provide\",\"parameters\":[]}" +
			"]}]}]}]";

		var widget = Button(Guid.NewGuid(), flows, ProviderData("blk-1", ["a"]));
		var (service, registry) = Build(widget);
		registry.Add(new FakeIntegration { Id = "integration", Actions = [action] });

		var resolution = await service.Resolve(widget.Id);

		Assert.That(resolution!.StateId, Is.EqualTo("a"));
	}

	[Test]
	public async Task DisabledBlock_RendersUnavailable()
	{
		var action = new FakeStateProviderAction
		{
			Id = "provide", SnapshotToReturn = new ActionStateSnapshot([new ActionStateDefinition("a", "A")], "a")
		};
		var widget = Button(Guid.NewGuid(), FlowWithBlock("blk-1", disabled: true), ProviderData("blk-1", ["a"]));
		var (service, registry) = Build(widget);
		registry.Add(new FakeIntegration { Id = "integration", Actions = [action] });

		var resolution = await service.Resolve(widget.Id);

		Assert.Multiple(() =>
		{
			Assert.That(action.GetActionStateCallCount, Is.EqualTo(0), "a disabled block is never read");
			Assert.That(resolution, Is.Not.Null, "renders as the fallback state, not null");
		});
	}

	[Test]
	public async Task MissingBlock_RendersUnavailable()
	{
		var action = new FakeStateProviderAction
		{
			Id = "provide", SnapshotToReturn = new ActionStateSnapshot([new ActionStateDefinition("a", "A")], "a")
		};
		var widget = Button(Guid.NewGuid(), FlowWithBlock("some-other-block"), ProviderData("blk-1", ["a"]));
		var (service, registry) = Build(widget);
		registry.Add(new FakeIntegration { Id = "integration", Actions = [action] });

		var resolution = await service.Resolve(widget.Id);

		Assert.Multiple(() =>
		{
			Assert.That(action.GetActionStateCallCount, Is.EqualTo(0));
			Assert.That(resolution, Is.Not.Null);
		});
	}

	[Test]
	public async Task UnregisteredIntegration_RendersUnavailable()
	{
		var widget = Button(Guid.NewGuid(), FlowWithBlock("blk-1"), ProviderData("blk-1", ["a"]));
		var (service, _) = Build(widget); // registry has nothing registered at all

		var resolution = await service.Resolve(widget.Id);

		Assert.That(resolution, Is.Not.Null);
	}

	[Test]
	public async Task DisabledIntegration_RendersUnavailable()
	{
		var action = new FakeStateProviderAction
		{
			Id = "provide", SnapshotToReturn = new ActionStateSnapshot([new ActionStateDefinition("a", "A")], "a")
		};
		var widget = Button(Guid.NewGuid(), FlowWithBlock("blk-1"), ProviderData("blk-1", ["a"]));
		var (service, registry) = Build(widget);
		registry.Add(new FakeIntegration { Id = "integration", Actions = [action] });
		registry.SetEnabled("integration", false);

		var resolution = await service.Resolve(widget.Id);

		Assert.Multiple(() =>
		{
			Assert.That(action.GetActionStateCallCount, Is.EqualTo(0));
			Assert.That(resolution, Is.Not.Null);
		});
	}

	[Test]
	public async Task NonProviderAction_RendersUnavailable()
	{
		// A real action that simply does not implement IStateProviderActionDefinition.
		var plainAction = new CapturingActionDefinition { Id = "provide" };
		var widget = Button(Guid.NewGuid(), FlowWithBlock("blk-1"), ProviderData("blk-1", ["a"]));
		var (service, registry) = Build(widget);
		registry.Add(new FakeIntegration { Id = "integration", Actions = [plainAction] });

		var resolution = await service.Resolve(widget.Id);

		Assert.Multiple(() =>
		{
			Assert.That(plainAction.ExecuteCount, Is.EqualTo(0));
			Assert.That(resolution, Is.Not.Null);
		});
	}

	// C6: the stored shape has exactly one stateProvider object - switching which block provides state
	// (blk-1 -> blk-2) replaces it outright, never leaving a second one marked.
	[Test]
	public void OnlyOneActionInstancePerButtonProvidesState()
	{
		var widget = Button(Guid.NewGuid(), FlowWithBlock("blk-2"), ProviderData("blk-2", ["a"]));

		var model = Application.Widgets.ActionButtonStateModel.Read(widget.Data);

		Assert.Multiple(() =>
		{
			Assert.That(model.StateProvider!.BlockId, Is.EqualTo("blk-2"));
			Assert.That(model.StateProvider.BlockId, Is.Not.EqualTo("blk-1"));
		});
	}

	// C7: two blocks configured for the same integration/action (e.g. "device A" and "device B") are
	// distinguished by block id, not by integration+action alone - the provider is read from exactly
	// the block stateProvider.blockId names, with that block's own parameters.
	[Test]
	public async Task TwoInstancesOfTheSameActionType_AreDistinguishedByBlockId()
	{
		var action = new FakeStateProviderAction
		{
			Id = "provide", SnapshotToReturn = new ActionStateSnapshot([new ActionStateDefinition("a", "A")], "a")
		};
		const string flows =
			"[{\"triggerId\":\"t\",\"triggerType\":\"onEvent\",\"children\":[" +
			"{\"id\":\"blk-a\",\"type\":\"action\",\"blockType\":\"integration.provide\"," +
			"\"integrationId\":\"integration\",\"actionId\":\"provide\"," +
			"\"parameters\":[{\"name\":\"device\",\"type\":\"string\",\"value\":\"A\"}]}," +
			"{\"id\":\"blk-b\",\"type\":\"action\",\"blockType\":\"integration.provide\"," +
			"\"integrationId\":\"integration\",\"actionId\":\"provide\"," +
			"\"parameters\":[{\"name\":\"device\",\"type\":\"string\",\"value\":\"B\"}]}" +
			"]}]";
		var widget = Button(Guid.NewGuid(), flows, ProviderData("blk-a", ["a"]));
		var (service, registry) = Build(widget);
		registry.Add(new FakeIntegration { Id = "integration", Actions = [action] });

		await service.Resolve(widget.Id);

		Assert.That(action.LastParameters, Is.Not.Null);
		Assert.That(action.LastParameters!["device"],
			Is.EqualTo("A"),
			"only ever called with block A's own parameters");

		// Deleting the other block (B) leaves the provider (A) intact.
		const string withoutB =
			"[{\"triggerId\":\"t\",\"triggerType\":\"onEvent\",\"children\":[" +
			"{\"id\":\"blk-a\",\"type\":\"action\",\"blockType\":\"integration.provide\"," +
			"\"integrationId\":\"integration\",\"actionId\":\"provide\"," +
			"\"parameters\":[{\"name\":\"device\",\"type\":\"string\",\"value\":\"A\"}]}" +
			"]}]";
		widget.Data = Button(widget.Id, withoutB, ProviderData("blk-a", ["a"])).Data;

		var stillResolved = await service.Resolve(widget.Id);
		Assert.That(stillResolved!.StateId, Is.EqualTo("a"));
	}

	// C8: reading state is parameters-only - never the action's ExecuteAsync side effect - and a
	// partial (settled-editor-draft) parameter set is tolerated rather than requiring every declared
	// parameter to be filled in.
	[Test]
	public async Task ProviderSnapshotIsReadFromParametersOnly_AndPartialConfigurationIsAllowed()
	{
		var action = new FakeStateProviderAction { Id = "provide", SnapshotToReturn = null };
		const string flows =
			"[{\"triggerId\":\"t\",\"triggerType\":\"onEvent\",\"children\":[" +
			"{\"id\":\"blk-1\",\"type\":\"action\",\"blockType\":\"integration.provide\"," +
			"\"integrationId\":\"integration\",\"actionId\":\"provide\"," +
			"\"parameters\":[{\"name\":\"device\",\"type\":\"string\",\"value\":\"A\"}]}" +
			"]}]";
		var widget = Button(Guid.NewGuid(), flows, ProviderData("blk-1", ["a"]));
		var (service, registry) = Build(widget);
		registry.Add(new FakeIntegration { Id = "integration", Actions = [action] });

		var resolution = await service.Resolve(widget.Id);

		Assert.Multiple(() =>
		{
			Assert.That(action.ExecuteCount, Is.EqualTo(0), "ExecuteAsync is never invoked to read state");
			Assert.That(action.LastParameters, Is.Not.Null);
			Assert.That(action.LastParameters!.ContainsKey("device"), Is.True);
			Assert.That(resolution, Is.Not.Null, "a null snapshot is accepted silently, not thrown");
		});
	}

	// C9: an empty States, or an ActiveStateId absent from States, is a misbehaving snapshot - ignored
	// rather than applied, keeping the last valid snapshot in place.
	[TestCase(true)]
	[TestCase(false)]
	public async Task MisbehavingProviderSnapshot_IsIgnoredRatherThanApplied(bool emptyStates)
	{
		var action = new FakeStateProviderAction
		{
			Id = "provide",
			SnapshotToReturn
				= new ActionStateSnapshot([new ActionStateDefinition("a", "A"), new ActionStateDefinition("b", "B")],
					"a")
		};
		var widget = Button(Guid.NewGuid(), FlowWithBlock("blk-1"), ProviderData("blk-1", ["a", "b"]));
		var readiness = new StartupReadiness();
		readiness.MarkCachesReady();
		readiness.MarkVariablesReady();
		var registry = new FakeIntegrationRegistry();
		registry.Add(new FakeIntegration { Id = "integration", Actions = [action] });
		var renderer = new VariableTemplateRenderer(new VariableRegistry());
		var derivedStore = new WidgetDerivedStateStore();
		var service = new WidgetStateService(new SingleWidgetFolderCache(widget),
			renderer,
			new ActionConditionEvaluator(renderer),
			registry,
			derivedStore,
			readiness);

		var first = await service.Resolve(widget.Id);
		Assert.That(first!.StateId, Is.EqualTo("a"));
		derivedStore.GetAndSet(widget.Id, "a"); // as if a prior successful reconcile had run

		action.SnapshotToReturn = emptyStates
			? new ActionStateSnapshot([], null)
			: new ActionStateSnapshot([new ActionStateDefinition("a", "A"), new ActionStateDefinition("b", "B")],
				"missing");

		var second = await service.Resolve(widget.Id);

		Assert.Multiple(() =>
		{
			Assert.That(second!.StateId,
				Is.EqualTo("a"),
				"the last valid snapshot is held, not an empty set or dangling id");
			Assert.That(second.States.Select(s => s.Id), Is.EquivalentTo(_aThenB), "never an empty set");
		});
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
}
