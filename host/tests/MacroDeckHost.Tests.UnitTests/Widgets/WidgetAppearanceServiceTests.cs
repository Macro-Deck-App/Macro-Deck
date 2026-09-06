using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Widgets.ActionButton;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Widgets;

[TestFixture]
public class WidgetAppearanceServiceTests
{
	private static readonly Guid _widgetId = Guid.Parse("11111111-1111-1111-1111-111111111111");
	private static readonly string[] _offThenOn = ["off", "on"];
	private static readonly string[] _lowMidHigh = ["low", "mid", "high"];

	[Test]
	public async Task AppearanceChange_WritesTheStoredWidget()
	{
		var fixture = new Fixture("""{"mode":"momentary","label":"configured"}""");

		var applied = await fixture.Service.ApplyAsync(Request("changed"));

		Assert.Multiple(() =>
		{
			Assert.That(applied, Is.True);
			Assert.That(fixture.Widget.Data, Does.Contain("changed").And.Not.Contain("configured"));
			Assert.That(fixture.Widgets.Updated, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task EveryWrite_TakesTheWidgetsDataLock()
	{
		var fixture = new Fixture("""{"mode":"momentary","label":"configured"}""");

		await fixture.Service.ApplyAsync(Request("changed"));

		Assert.That(fixture.WriteLock.Acquired, Is.EqualTo(new[] { _widgetId }));
	}

	[TestCase(true, "on")]
	[TestCase(false, "off")]
	public async Task CurrentState_OnABoundButton_FollowsTheDerivedState(bool derived, string expectedState)
	{
		var fixture = new Fixture("""
								  {"mode":"toggle","stateBinding":{"variable":"vars.mic"},
								  "states":{"off":{"label":"Off"},"on":{"label":"On"}}}
								  """);
		fixture.DerivedStates.GetAndSet(_widgetId, expectedState);

		await fixture.Service.ApplyAsync(Request("changed"));

		Assert.Multiple(() =>
		{
			Assert.That(StateLabel(fixture, expectedState), Is.EqualTo("changed"));
			Assert.That(StateLabel(fixture, expectedState == "on" ? "off" : "on"), Is.Not.EqualTo("changed"));
		});
	}

	// Before the reconciler has ever run for this widget, the derived store has nothing cached
	// (issue #312). ResolveStates' documented fallback is the just-upgraded activeStateId derived from
	// the legacy isToggled bit - the best information actually available - rather than an uninformed
	// states[0], which is why "current" here lands on "on" (isToggled was true), not "off".
	[Test]
	public async Task CurrentState_OnABoundButton_FallsBackToTheStoredToggle_WhenNoDerivedStateIsKnown()
	{
		var fixture = new Fixture("""
								  {"mode":"toggle","isToggled":true,"stateBinding":{"variable":"vars.mic"},
								  "states":{"off":{"label":"Off"},"on":{"label":"On"}}}
								  """);

		await fixture.Service.ApplyAsync(Request("changed"));

		Assert.Multiple(() =>
		{
			Assert.That(StateLabel(fixture, "on"), Is.EqualTo("changed"));
			Assert.That(StateLabel(fixture, "off"), Is.EqualTo("Off"), "the other state must be untouched");
		});
	}

	[Test]
	public async Task CurrentState_OnAnUnboundButton_StillFollowsTheStoredToggle()
	{
		var fixture = new Fixture("""
								  {"mode":"toggle","isToggled":true,"states":{"off":{"label":"Off"},"on":{"label":"On"}}}
								  """);

		await fixture.Service.ApplyAsync(Request("changed"));

		Assert.That(StateLabel(fixture, "on"), Is.EqualTo("changed"));
	}

	[Test]
	public async Task UnknownWidget_IsReportedRatherThanThrown()
	{
		var fixture = new Fixture("{}");

		var applied = await fixture.Service.ApplyAsync(new WidgetAppearanceRequest
		{
			WidgetId = Guid.NewGuid().ToString(),
			Patch = new WidgetAppearancePatch { Label = "x" }
		});

		Assert.Multiple(() =>
		{
			Assert.That(applied, Is.False);
			Assert.That(fixture.Widgets.Updated, Is.Empty);
		});
	}

	[Test]
	public async Task TargetThatIsNotAnId_IsReportedRatherThanThrown()
	{
		var fixture = new Fixture("{}");

		var applied = await fixture.Service.ApplyAsync(new WidgetAppearanceRequest
		{
			WidgetId = "not-a-guid",
			Patch = new WidgetAppearancePatch { Label = "x" }
		});

		Assert.That(applied, Is.False);
	}

	[Test]
	public async Task AppearanceChange_RoundTripsLineBreaksAndLiquidUnchanged()
	{
		const string label = "Line one\nLine two\n{{ vars.system_cpu_usage_percent }}%";
		var fixture = new Fixture("""{"mode":"momentary","label":"configured"}""");

		await fixture.Service.ApplyAsync(new WidgetAppearanceRequest
		{
			WidgetId = _widgetId.ToString(),
			Patch = new WidgetAppearancePatch { Label = label }
		});

		var storedLabel = WidgetAppearanceJson.ReadLabel((JsonObject)JsonNode.Parse(fixture.Widget.Data!)!,
			WidgetTypeIds.ActionButton,
			"off");
		Assert.That(storedLabel, Is.EqualTo(label));
	}

	[Test]
	public void GetWidgets_QualifiesEachWidgetByProfileAndFolder()
	{
		var fixture = new Fixture("""{"mode":"momentary","label":"Mute"}""");

		var widget = fixture.Service.GetWidgets().Single();

		Assert.Multiple(() =>
		{
			Assert.That(widget.Label, Is.EqualTo("Mute"));
			Assert.That(widget.Location, Is.EqualTo("Main / Home"));
		});
	}

	[Test]
	public void GetWidgets_FallsBackToTheTypeWhenAWidgetHasNoLabel()
	{
		var fixture = new Fixture("{}", WidgetTypeIds.Clock);

		Assert.That(fixture.Service.GetWidgets().Single().Label, Is.EqualTo("Clock"));
	}

	// Scenario F6: States and CurrentStateId are reported for any State-Mode button, and the obsolete
	// HasOnOffStates flag still answers - true for two or more states whatever their ids (resolution
	// 9), not only a literal on/off pair, so an old plugin keeps the capability it has today.
	[Test]
	public void WidgetTargetInfo_ReportsStatesAndCurrentStateIdWhileHasOnOffStatesStillAnswers()
	{
		var toggle = new Fixture("""{"mode":"toggle"}""");
		var threeState = new Fixture(
			"""{"stateMode":true,"states":[{"id":"low","label":"Low"},{"id":"mid","label":"Mid"},{"id":"high","label":"High"}],"activeStateId":"mid"}""");
		var momentary = new Fixture("""{"mode":"momentary"}""");
		var nonButton = new Fixture("{}", WidgetTypeIds.Clock);

#pragma warning disable CS0618 // HasOnOffStates is deprecated but must keep answering for an old plugin.
		Assert.Multiple(() =>
		{
			var toggleInfo = toggle.Service.GetWidgets().Single();
			Assert.That(toggleInfo.HasOnOffStates, Is.True);
			Assert.That(toggleInfo.States.Select(s => s.Id), Is.EqualTo(_offThenOn));
			Assert.That(toggleInfo.CurrentStateId, Is.EqualTo("off"));

			var threeStateInfo = threeState.Service.GetWidgets().Single();
			Assert.That(threeStateInfo.HasOnOffStates, Is.True, "two or more states, whatever their ids");
			Assert.That(threeStateInfo.States.Select(s => s.Id), Is.EqualTo(_lowMidHigh));
			Assert.That(threeStateInfo.CurrentStateId, Is.EqualTo("mid"));

			Assert.That(momentary.Service.GetWidgets().Single().HasOnOffStates, Is.False);
			Assert.That(momentary.Service.GetWidgets().Single().States, Is.Empty);
			Assert.That(nonButton.Service.GetWidgets().Single().HasOnOffStates, Is.False);
		});
#pragma warning restore CS0618
	}

	// The compatibility guarantee WidgetAppearanceService.ApplyAsync exists for: a widget still stored
	// in the pre-#612 mode:"toggle" + states:{off,on} shape is normalized before its states are
	// resolved, so a plugin restyling it by state id lands on the correct state rather than failing or
	// silently no-opping against data ApplyAsync never upgraded.
	[Test]
	public async Task LegacyToggleBag_RestyledByStateId_IsNormalizedBeforeApplying()
	{
		var fixture = new Fixture(
			"""{"mode":"toggle","isToggled":false,"states":{"off":{"label":"Off"},"on":{"label":"On"}}}""");

		var applied = await fixture.Service.ApplyAsync(new WidgetAppearanceRequest
		{
			WidgetId = _widgetId.ToString(),
			Patch = new WidgetAppearancePatch { Label = "Live" },
			StateIds = ["on"]
		});

		Assert.Multiple(() =>
		{
			Assert.That(applied, Is.True);
			Assert.That(StateLabel(fixture, "on"), Is.EqualTo("Live"));
			Assert.That(StateLabel(fixture, "off"), Is.EqualTo("Off"), "the other state must be untouched");
			Assert.That(fixture.Widget.Data, Does.Not.Contain("\"mode\""), "the legacy key must not survive the write");
		});
	}

	// Acceptance Group B, scenario 7: Set Icon writes the typed shape through the unchanged string patch
	// API - an object with "type" present, never a bare string, and no stray legacy iconId key beside it.
	[Test]
	public async Task SetIcon_WritesTheTypedIconShapeThroughTheUnchangedStringPatchApi()
	{
		var fixture = new Fixture(
			"""{"stateMode":true,"states":[{"id":"off","label":"Off","appearance":{}},{"id":"on","label":"On","appearance":{}}]}""");

		var applied = await fixture.Service.ApplyAsync(new WidgetAppearanceRequest
		{
			WidgetId = _widgetId.ToString(),
			Patch = new WidgetAppearancePatch { IconId = "0198bbbb-1111-2222-3333-444444444444" },
			StateIds = ["on"]
		});

		var onAppearance = StateAppearance(fixture, "on");
		Assert.Multiple(() =>
		{
			Assert.That(applied, Is.True);
			Assert.That(onAppearance!["icon"], Is.Not.Null.And.TypeOf<JsonObject>());
			Assert.That(onAppearance["icon"]!["type"]!.GetValue<string>(), Is.EqualTo("icon-pack"));
			Assert.That(onAppearance["icon"]!["reference"]!.GetValue<string>(),
				Is.EqualTo("0198bbbb-1111-2222-3333-444444444444"));
			Assert.That(onAppearance.ContainsKey("iconId"), Is.False, "no stray legacy iconId key beside it");
		});
	}

	// Acceptance Group B, scenario 8: the documented empty-string clear still clears - the icon key is
	// removed outright, never a stub {"type":"icon-pack","reference":""}.
	[Test]
	public async Task SetIcon_EmptyStringStillClearsTheIconKeyOutright()
	{
		var fixture = new Fixture(
			"""{"stateMode":true,"states":[{"id":"off","label":"Off","appearance":{}},{"id":"on","label":"On","appearance":{"icon":{"type":"icon-pack","reference":"0198aaaa-1111-2222-3333-444444444444"}}}]}""");

		var applied = await fixture.Service.ApplyAsync(new WidgetAppearanceRequest
		{
			WidgetId = _widgetId.ToString(),
			Patch = new WidgetAppearancePatch { IconId = string.Empty },
			StateIds = ["on"]
		});

		var onAppearance = StateAppearance(fixture, "on");
		Assert.Multiple(() =>
		{
			Assert.That(applied, Is.True);
			Assert.That(onAppearance!.ContainsKey("icon"), Is.False);
		});

		var rendered = ActionButtonWidgetData.Parse(JsonDocument.Parse(fixture.Widget.Data!).RootElement)
			.ResolveIcon("on");
		Assert.That(rendered, Is.Null, "renders no image");
	}

	// Acceptance Group A, scenario 5 (counterexample): an unknown provider type keeps the widget usable -
	// editing an unrelated property (the label) and saving must not touch or reject the icon object.
	[Test]
	public async Task UnknownProviderTypeIcon_SurvivesAnUnrelatedLabelEditUnchanged()
	{
		var fixture = new Fixture(
			"""{"stateMode":false,"icon":{"type":"future-provider","reference":"x"},"label":"Old"}""");

		var applied = await fixture.Service.ApplyAsync(Request("Renamed"));

		var data = (JsonObject)JsonNode.Parse(fixture.Widget.Data!)!;
		Assert.Multiple(() =>
		{
			Assert.That(applied, Is.True);
			Assert.That(data["label"]!.GetValue<string>(), Is.EqualTo("Renamed"));
			Assert.That(data["icon"]!["type"]!.GetValue<string>(), Is.EqualTo("future-provider"));
			Assert.That(data["icon"]!["reference"]!.GetValue<string>(), Is.EqualTo("x"));
		});
	}

	// Acceptance Group A, scenario 6: an unknown icon-pack reference is never helpfully cleared by a save.
	[Test]
	public async Task DanglingIconPackReference_IsStillStoredAfterAnUnrelatedSave()
	{
		var fixture = new Fixture(
			"""{"stateMode":false,"icon":{"type":"icon-pack","reference":"0198dddd-0000-0000-0000-000000000000"},"label":"Old"}""");

		var applied = await fixture.Service.ApplyAsync(Request("Renamed"));

		var data = (JsonObject)JsonNode.Parse(fixture.Widget.Data!)!;
		Assert.Multiple(() =>
		{
			Assert.That(applied, Is.True);
			Assert.That(data["label"]!.GetValue<string>(), Is.EqualTo("Renamed"));
			Assert.That(data["icon"]!["reference"]!.GetValue<string>(),
				Is.EqualTo("0198dddd-0000-0000-0000-000000000000"));
		});
	}

	// Acceptance Group B, scenario 9 (counterexample): Set Icon fails on a provider-controlled button and
	// changes nothing - the action reports failure (ApplyAsync returns false for this icon-only patch),
	// and the stored icon is unaffected.
	[Test]
	public async Task SetIcon_FailsOnAProviderControlledButton_AndTheStoredIconIsUnaffected()
	{
		var fixture = new Fixture("""
								  {"stateMode":true,"states":[{"id":"off","label":"Off","appearance":{}},
								  {"id":"on","label":"On","appearance":{"icon":{"type":"icon-pack","reference":"0198aaaa-1111-2222-3333-444444444444"}}}],
								  "iconProvider":{"blockId":"blk-1","integrationId":"spotify","actionId":"current-track"},
								  "flows":"[{\"triggerId\":\"t\",\"triggerType\":\"onShortPress\",\"children\":[{\"id\":\"blk-1\",\"type\":\"action\",\"blockType\":\"spotify.current-track\",\"integrationId\":\"spotify\",\"actionId\":\"current-track\",\"parameters\":[]}]}]"}
								  """);

		var applied = await fixture.Service.ApplyAsync(new WidgetAppearanceRequest
		{
			WidgetId = _widgetId.ToString(),
			Patch = new WidgetAppearancePatch { IconId = "0198bbbb-1111-2222-3333-444444444444" },
			StateIds = ["on"]
		});

		var onAppearance = StateAppearance(fixture, "on");
		Assert.Multiple(() =>
		{
			Assert.That(applied, Is.False, "Set Icon must report failure, not silent success");
			Assert.That(onAppearance!["icon"]!["reference"]!.GetValue<string>(),
				Is.EqualTo("0198aaaa-1111-2222-3333-444444444444"),
				"the stored icon is unaffected");
		});
	}

	// Acceptance Group B, scenario 9's decision-2 worked example: a mixed Label+Icon patch on a
	// provider-controlled button applies the label, drops the icon, and returns true.
	[Test]
	public async Task SetIconAndLabelTogether_OnAProviderControlledButton_AppliesTheLabelButDropsTheIcon()
	{
		var fixture = new Fixture("""
								  {"stateMode":true,"states":[{"id":"off","label":"Off","appearance":{}},
								  {"id":"on","label":"On","appearance":{"label":"Old","icon":{"type":"icon-pack","reference":"0198aaaa-1111-2222-3333-444444444444"}}}],
								  "iconProvider":{"blockId":"blk-1","integrationId":"spotify","actionId":"current-track"},
								  "flows":"[{\"triggerId\":\"t\",\"triggerType\":\"onShortPress\",\"children\":[{\"id\":\"blk-1\",\"type\":\"action\",\"blockType\":\"spotify.current-track\",\"integrationId\":\"spotify\",\"actionId\":\"current-track\",\"parameters\":[]}]}]"}
								  """);

		var applied = await fixture.Service.ApplyAsync(new WidgetAppearanceRequest
		{
			WidgetId = _widgetId.ToString(),
			Patch = new WidgetAppearancePatch { Label = "New", IconId = "0198bbbb-1111-2222-3333-444444444444" },
			StateIds = ["on"]
		});

		var onAppearance = StateAppearance(fixture, "on");
		Assert.Multiple(() =>
		{
			Assert.That(applied, Is.True, "the label change alone still applies and is reported");
			Assert.That(onAppearance!["label"]!.GetValue<string>(), Is.EqualTo("New"));
			Assert.That(onAppearance["icon"]!["reference"]!.GetValue<string>(),
				Is.EqualTo("0198aaaa-1111-2222-3333-444444444444"),
				"the icon property is dropped from the patch exactly as an unsupported property is");
		});
	}

	// Acceptance Group B, scenario 10 (counterexample, pairs with 9): a button with only a state provider
	// is unaffected - a shared HasActiveProvider(widget) guard would wrongly fail this.
	[Test]
	public async Task SetIcon_StillSucceeds_OnAButtonThatOnlyHasAStateProvider()
	{
		var fixture = new Fixture("""
								  {"stateMode":true,"states":[{"id":"off","label":"Off","appearance":{}},
								  {"id":"on","label":"On","appearance":{"icon":{"type":"icon-pack","reference":"0198aaaa-1111-2222-3333-444444444444"}}}],
								  "stateProvider":{"blockId":"blk-1","integrationId":"spotify","actionId":"now-playing"}}
								  """);

		var applied = await fixture.Service.ApplyAsync(new WidgetAppearanceRequest
		{
			WidgetId = _widgetId.ToString(),
			Patch = new WidgetAppearancePatch { IconId = "0198bbbb-1111-2222-3333-444444444444" },
			StateIds = ["on"]
		});

		var onAppearance = StateAppearance(fixture, "on");
		Assert.Multiple(() =>
		{
			Assert.That(applied, Is.True);
			Assert.That(onAppearance!["icon"]!["reference"]!.GetValue<string>(),
				Is.EqualTo("0198bbbb-1111-2222-3333-444444444444"));
		});
	}

	[Test]
	public void GetWidgets_ReportsAppearanceCapabilitiesFromTheSharedJsonMapping()
	{
		var button = new Fixture("{}");
		var slider = new Fixture("{}", WidgetTypeIds.Slider);
		var clock = new Fixture("{}", WidgetTypeIds.Clock);

		Assert.Multiple(() =>
		{
			Assert.That(button.Service.GetWidgets().Single().AppearanceProperties,
				Does.Contain(WidgetAppearanceProperty.Font).And.Contain(WidgetAppearanceProperty.Icon));
			Assert.That(button.Service.GetWidgets().Single().AppearanceProperties,
				Does.Contain(WidgetAppearanceProperty.IconDisplay));
			Assert.That(slider.Service.GetWidgets().Single().AppearanceProperties,
				Does.Contain(WidgetAppearanceProperty.Icon).And.Not.Contain(WidgetAppearanceProperty.IconDisplay));
			Assert.That(clock.Service.GetWidgets().Single().AppearanceProperties,
				Is.EqualTo(new[] { WidgetAppearanceProperty.Border, WidgetAppearanceProperty.BorderColor }));
		});
	}

	/// <summary>Reads a state's appearance label by id from the persisted array shape.</summary>
	private static string? StateLabel(Fixture fixture, string stateId)
	{
		var states = (JsonArray)((JsonObject)JsonNode.Parse(fixture.Widget.Data!)!)["states"]!;
		var entry = states.OfType<JsonObject>().First(e => e["id"]!.GetValue<string>() == stateId);
		return entry["appearance"]?["label"]?.GetValue<string>();
	}

	/// <summary>Reads a state's whole appearance object by id from the persisted array shape.</summary>
	private static JsonObject? StateAppearance(Fixture fixture, string stateId)
	{
		var states = (JsonArray)((JsonObject)JsonNode.Parse(fixture.Widget.Data!)!)["states"]!;
		var entry = states.OfType<JsonObject>().First(e => e["id"]!.GetValue<string>() == stateId);
		return entry["appearance"] as JsonObject;
	}

#pragma warning disable CS0618 // Exercises the pre-existing selector-based request, not yet migrated to StateIds.
	private static WidgetAppearanceRequest ClearRequest(
		WidgetAppearanceProperty property,
		WidgetStateSelector state = WidgetStateSelector.Current)
		=> new()
		{
			WidgetId = _widgetId.ToString(),
			Patch = new WidgetAppearancePatch(),
			ClearProperties = [property],
			State = state
		};
#pragma warning restore CS0618

	private static WidgetAppearanceRequest Request(string label)
		=> new()
		{
			WidgetId = _widgetId.ToString(),
			Patch = new WidgetAppearancePatch { Label = label }
		};

	private sealed class Fixture
	{
		public Fixture(string data, string type = WidgetTypeIds.ActionButton)
		{
			var profileId = Guid.NewGuid();
			Widget = new WidgetEntity { Id = _widgetId, Type = type, Data = data };
			var folder = new FolderEntity
			{
				Id = Guid.NewGuid(),
				ProfileId = profileId,
				Name = "Home",
				Order = 0,
				Widgets = [Widget]
			};

			Widgets = new RecordingWidgetService();
			WriteLock = new RecordingWriteLock();
			DerivedStates = new WidgetDerivedStateStore();

			Service = new WidgetAppearanceService(new SingleFolderCache(folder),
				new SingleProfileCache(profileId, "Main"),
				Widgets,
				WriteLock,
				DerivedStates,
				new LoggerConfiguration().CreateLogger());
		}

		public WidgetEntity Widget { get; }
		public RecordingWidgetService Widgets { get; }
		public RecordingWriteLock WriteLock { get; }
		public WidgetDerivedStateStore DerivedStates { get; }
		public WidgetAppearanceService Service { get; }
	}

	private sealed class RecordingWriteLock : IWidgetDataWriteLock
	{
		private readonly WidgetDataWriteLock _inner = new();

		public List<Guid> Acquired { get; } = [];

		public Task<IDisposable> AcquireAsync(Guid widgetId, CancellationToken cancellationToken = default)
		{
			Acquired.Add(widgetId);
			return _inner.AcquireAsync(widgetId, cancellationToken);
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

	private sealed class SingleFolderCache : IFolderCache
	{
		private readonly FolderEntity _folder;

		public SingleFolderCache(FolderEntity folder)
		{
			_folder = folder;
		}

		public List<FolderEntity> GetAllFolders() => [_folder];
		public FolderEntity? GetFolderById(Guid id) => id == _folder.Id ? _folder : null;
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

	private sealed class SingleProfileCache : IProfileCache
	{
		private readonly ProfileEntity _profile;

		public SingleProfileCache(Guid id, string name)
		{
			_profile = new ProfileEntity { Id = id, Name = name };
		}

		public bool HadUnreadableProfiles => false;
		public ProfileEntity? GetById(Guid id) => id == _profile.Id ? _profile : null;
		public List<ProfileEntity> GetAll() => [_profile];
		public Task InitializeCache() => Task.CompletedTask;
		public Task AddOrUpdate(ProfileEntity profile) => Task.CompletedTask;

		public Task AddOrUpdateAggregate(ProfileEntity profile, IReadOnlyCollection<FolderEntity> folders)
			=> Task.CompletedTask;

		public Task Remove(Guid id) => Task.CompletedTask;
	}
}
