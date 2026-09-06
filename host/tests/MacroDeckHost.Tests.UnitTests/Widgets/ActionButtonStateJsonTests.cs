using System.Text.Json;
using System.Text.Json.Nodes;
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
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Tests.UnitTests.Widgets;

/// <summary>
/// The set-aside invariants for provider adoption and restore (scenarios C2, C4, C5).
///
/// <para>
/// C1 ("activating a provider stashes states and mapping and active state") and C3 ("removing a
/// provider with nothing stashed falls back to default Off/On") are NOT covered here: no host
/// function performs either transform. <see cref="ActionButtonStateJson" /> only ever *restores* a
/// stash that already exists (<see cref="ActionButtonStateJson.RestoreManualBackup" />) or reconciles
/// an already-active provider's live state set (<see cref="ActionButtonStateJson.AdoptProviderStates" />);
/// nothing writes <c>manualStateBackup</c>, and nothing synthesizes a default pair when a provider
/// disappears with nothing stashed. Given the Angular editor's G2 ("enabling seeds ids exactly
/// ['off','on']") and G8 ("removal restores") already own the seed-a-default and stash-before-adopting
/// UI flow, C1/C3 read as that same client-side responsibility, not a host gap - but the host has no
/// symmetric primitive to test either way, so this is flagged rather than tested against a
/// nonexistent function.
/// </para>
/// </summary>
[TestFixture]
public class ActionButtonStateJsonTests
{
	private static readonly string[] _critThenOk = ["crit", "ok"];
	private static readonly string[] _ruleOneId = ["rule-1"];
	private static readonly string[] _liveOfflineStarting = ["live", "offline", "starting"];

	// Regression for issue #718: removing a mapping (deleting its rules) left a rules-less stateMapping
	// behind that Normalize never cleaned up, permanently locking out Set/Cycle State afterwards.
	[Test]
	public void Normalize_RemovesARulesLessStateMapping_SoRemovingAMappingFullyRestoresTheButton()
	{
		var data = (JsonObject)JsonNode.Parse("""
											  {
											    "stateMode": true,
											    "states": [{"id":"a","label":"A"},{"id":"b","label":"B"}],
											    "activeStateId": "a",
											    "stateMapping": {"rules": [], "fallbackStateId": "a"}
											  }
											  """)!;

		ActionButtonStateJson.Normalize(data);

		Assert.That(data["stateMapping"], Is.Null);
	}

	[Test]
	public void Normalize_LeavesAMappingWithARealRuleUnchanged()
	{
		var data = (JsonObject)JsonNode.Parse("""
											  {
											    "stateMode": true,
											    "states": [{"id":"a","label":"A"},{"id":"b","label":"B"}],
											    "activeStateId": "a",
											    "stateMapping": {
											      "rules": [{"id":"r1","stateId":"b","when":{"kind":"compare","left":{"$var":"cpu"},"operator":">=","right":90}}],
											      "fallbackStateId": "a"
											    }
											  }
											  """)!;
		var expectedMapping = data["stateMapping"]!.DeepClone();

		ActionButtonStateJson.Normalize(data);

		Assert.That(JsonNode.DeepEquals(data["stateMapping"], expectedMapping), Is.True);
	}

	// A stateMapping of a shape other than an object (here a string) is left alone so the existing schema
	// validation keeps rejecting it as malformed - PruneDegenerateMapping must not silently "fix" it.
	[Test]
	public void Normalize_LeavesANonObjectStateMappingInPlaceForSchemaValidationToReject()
	{
		var data = (JsonObject)JsonNode.Parse("""
											  {
											    "stateMode": true,
											    "states": [{"id":"a","label":"A"}],
											    "stateMapping": "not-an-object"
											  }
											  """)!;

		ActionButtonStateJson.Normalize(data);

		Assert.That(data["stateMapping"]?.GetValueKind(), Is.EqualTo(JsonValueKind.String));
	}

	// A stateMapping that is a well-formed object but whose "rules" field is itself the wrong shape
	// (here a string instead of an array) must also be left alone, for the same reason as the
	// non-object case above: deleting the key would silently swallow a schema violation
	// ("rules: {type: array}") that validation is supposed to report.
	[Test]
	public void Normalize_LeavesAStateMappingWithNonArrayRulesInPlaceForSchemaValidationToReject()
	{
		var data = (JsonObject)JsonNode.Parse("""
											  {
											    "stateMode": true,
											    "states": [{"id":"a","label":"A"}],
											    "stateMapping": {"rules": "oops", "fallbackStateId": "a"}
											  }
											  """)!;

		ActionButtonStateJson.Normalize(data);

		Assert.That(data["stateMapping"], Is.Not.Null);
		Assert.That(data["stateMapping"]!["rules"]?.GetValueKind(), Is.EqualTo(JsonValueKind.String));
	}

	[Test]
	public void RemovingTheProvider_RestoresTheStashedManualConfigurationExactly()
	{
		var data = (JsonObject)JsonNode.Parse("""
											  {
											    "stateMode": true,
											    "states": [{"id":"live","label":"Live","appearance":{}}],
											    "stateProvider": {"blockId":"blk-1","integrationId":"i","actionId":"a","states":[{"id":"live","label":"Live"}]},
											    "manualStateBackup": {
											      "states": [
											        {"id":"crit","label":"Critical","appearance":{"backgroundColor":"#ff0000"}},
											        {"id":"ok","label":"OK","appearance":{"backgroundColor":"#00ff00"}}
											      ],
											      "stateMapping": {
											        "rules": [{"id":"rule-1","stateId":"crit","when":{"kind":"compare","left":{"$var":"cpu"},"operator":">=","right":90}}],
											        "fallbackStateId": "ok"
											      },
											      "activeStateId": "ok"
											    }
											  }
											  """)!;

		var restored = ActionButtonStateJson.RestoreManualBackup(data);

		var model = ActionButtonStateModel.Read((JsonObject)data.DeepClone());
		Assert.Multiple(() =>
		{
			Assert.That(restored, Is.True);
			Assert.That(model.States.Select(s => s.Id), Is.EqualTo(_critThenOk));
			Assert.That(model.FindState("crit")!.Appearance!["backgroundColor"]!.GetValue<string>(),
				Is.EqualTo("#ff0000"));
			Assert.That(model.FindState("ok")!.Appearance!["backgroundColor"]!.GetValue<string>(),
				Is.EqualTo("#00ff00"));
			Assert.That(model.StateMapping!.Rules.Select(r => r.Id), Is.EqualTo(_ruleOneId));
			Assert.That(model.StateMapping.Rules[0].StateId, Is.EqualTo("crit"));
			Assert.That(model.StateMapping.FallbackStateId, Is.EqualTo("ok"));
			Assert.That(model.ActiveStateId, Is.EqualTo("ok"));
			Assert.That(model.StateProvider, Is.Null, "no provider id remains");
			Assert.That(data["manualStateBackup"], Is.Null);
		});
	}

	[Test]
	public void ProviderReconfigured_PreservesAppearanceByStableIdAcrossALabelChange()
	{
		var data = (JsonObject)JsonNode.Parse("""
											  {
											    "stateMode": true,
											    "states": [],
											    "stateProvider": {"blockId":"blk-1","integrationId":"i","actionId":"a","states":[]}
											  }
											  """)!;

		ActionButtonStateJson.AdoptProviderStates(data,
			[
				new ActionStateDefinition("live", "Live"),
				new ActionStateDefinition("offline", "Offline"),
				new ActionStateDefinition("unavailable", "Unavailable")
			],
			TestLocalization.Resolver,
			null);
		// The user restyles "live" once it is showing.
		var liveAppearance = data["states"]!.AsArray()
			.OfType<JsonObject>()
			.First(entry => entry["id"]!.GetValue<string>() == "live")["appearance"]!
			.AsObject();
		liveAppearance["backgroundColor"] = "#ff0000";

		// The provider is reconfigured: "live" keeps its id but is relabeled, "unavailable" vanishes,
		// and a brand new "starting" id appears - the counterexample for label- or position-keyed
		// appearance.
		ActionButtonStateJson.AdoptProviderStates(data,
			[
				new ActionStateDefinition("live", "Live Now"),
				new ActionStateDefinition("offline", "Offline"),
				new ActionStateDefinition("starting", "Starting")
			],
			TestLocalization.Resolver,
			null);

		var model = ActionButtonStateModel.Read((JsonObject)data.DeepClone());
		Assert.Multiple(() =>
		{
			Assert.That(model.FindState("live")!.Label, Is.EqualTo("Live Now"));
			Assert.That(model.FindState("live")!.Appearance!["backgroundColor"]!.GetValue<string>(),
				Is.EqualTo("#ff0000"),
				"appearance survives the label change because it is keyed by id, not label or position");
			Assert.That(model.FindState("starting")!.Appearance!["backgroundColor"],
				Is.Null,
				"a newly appearing id inherits nothing from a vanished one");
			// A provider defines the complete set, so an id it no longer offers is gone from the live
			// states - keeping it would show a state nothing can ever report.
			Assert.That(model.States.Select(s => s.Id), Is.EqualTo(_liveOfflineStarting));
		});
	}

	[Test]
	public async Task RenamingAStateLabel_KeepsIdAppearanceAndRuleReferences()
	{
		var data = (JsonObject)JsonNode.Parse("""
											  {
											    "stateMode": true,
											    "states": [
											      {"id":"off","label":"Off","appearance":{}},
											      {"id":"on","label":"On","appearance":{"backgroundColor":"#00ff00"}}
											    ],
											    "activeStateId": "on",
											    "stateMapping": {
											      "rules": [{"id":"r1","stateId":"on","when":{"kind":"compare","left":{"$var":"cpu"},"operator":">=","right":80}}],
											      "fallbackStateId": "off"
											    }
											  }
											  """)!;

		// Rename the "on" state's label - id, appearance and every rule reference are untouched.
		var onEntry = data["states"]!.AsArray().OfType<JsonObject>().First(e => e["id"]!.GetValue<string>() == "on");
		onEntry["label"] = "Streaming";

		var model = ActionButtonStateModel.Read((JsonObject)data.DeepClone());
		Assert.Multiple(() =>
		{
			Assert.That(model.FindState("on")!.Id, Is.EqualTo("on"));
			Assert.That(model.FindState("on")!.Label, Is.EqualTo("Streaming"));
			Assert.That(model.FindState("on")!.Appearance!["backgroundColor"]!.GetValue<string>(),
				Is.EqualTo("#00ff00"));
			Assert.That(model.StateMapping!.Rules[0].StateId, Is.EqualTo("on"));
		});

		var widget = new WidgetEntity
		{
			Id = Guid.NewGuid(), FolderId = Guid.NewGuid(), Type = WidgetTypeIds.ActionButton,
			Data = data.ToJsonString()
		};
		var registry = new VariableRegistry();
		registry.Upsert(new VariableEntity
		{
			Name = "cpu",
			Scope = VariableScope.Global,
			Type = VariableType.Numeric,
			Classification = VariableClassification.User,
			Value = "95"
		});
		var renderer = new VariableTemplateRenderer(registry);
		var readiness = new StartupReadiness();
		readiness.MarkCachesReady();
		readiness.MarkVariablesReady();
		var service = new WidgetStateService(new SingleWidgetFolderCache(widget),
			renderer,
			new ActionConditionEvaluator(renderer),
			new FakeIntegrationRegistry(),
			new WidgetDerivedStateStore(),
			readiness);

		var resolution = await service.Resolve(widget.Id);

		Assert.Multiple(() =>
		{
			Assert.That(resolution!.StateId, Is.EqualTo("on"), "vars.state stays the id");
			Assert.That(TestLocalization.Resolve(resolution.StateLabel),
				Is.EqualTo("Streaming"),
				"vars.stateLabel follows the rename");
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
