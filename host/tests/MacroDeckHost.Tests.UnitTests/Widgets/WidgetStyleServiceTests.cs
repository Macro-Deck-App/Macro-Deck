using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Widgets;

[TestFixture]
public class WidgetStyleServiceTests
{
	private const string StyledButton = """
										{"label":"Source","flows":"[]","backgroundColor":"#112233","labelColor":"#fefefe",
										"labelBoxColor":"#010101","labelShadow":false,"labelOutlineColor":"#ff0000",
										"labelOutlineWidth":2,"labelBoxBorderColor":"#00ff00","labelBoxBorderWidth":3,
										"fontFaceId":"font-a","fontSize":20,"textAlign":"left","labelPosition":"top",
										"border":{"style":"static","color":"#abcdef"},
										"icon":{"type":"pack","reference":"pack/source"},"iconColor":"#123456",
										"iconDisplay":{"fit":"cover","zoom":150}}
										""";

	private static readonly string[] _buttonStyleKeys =
	[
		"backgroundColor", "labelColor", "labelBoxColor", "labelShadow", "labelOutlineColor", "labelOutlineWidth",
		"labelBoxBorderColor", "labelBoxBorderWidth", "fontFaceId", "fontSize", "textAlign", "labelPosition", "border",
		"iconColor", "iconDisplay"
	];

	[Test]
	public async Task ButtonToButton_CopiesEveryVisualProperty_AndLeavesContentAlone()
	{
		var source = Button(1, StyledButton);
		var target = Button(2,
			"""
			{"label":"Target","flows":"[{\"id\":\"f1\"}]","backgroundColor":"#999999","fontSize":10,
			"icon":{"type":"pack","reference":"pack/target"},"iconColor":"#777777"}
			""");
		var untouched = Normalized(target.Data);
		var fixture = new Fixture(source, target);

		var outcome = await fixture.Service.PasteAsync(fixture.Service.Copy(source.Id), [target.Id]);

		var result = Data(target);
		var expected = Data(source);
		Assert.Multiple(() =>
		{
			Assert.That(outcome, Is.EqualTo(WidgetStylePasteOutcome.Applied));
			foreach (var key in _buttonStyleKeys)
			{
				Assert.That(Json(result[key]), Is.EqualTo(Json(expected[key])), key);
			}

			foreach (var key in new[] { "label", "flows", "icon" })
			{
				Assert.That(Json(result[key]), Is.EqualTo(Json(untouched[key])), key);
			}

			Assert.That(fixture.Widgets.Updated, Is.EqualTo(new[] { target }));
		});
	}

	[Test]
	public async Task PropertyTheSourceLacks_IsRemovedFromTheTarget()
	{
		var source = Button(1, """{"label":"Plain"}""");
		var target = Button(2,
			"""{"label":"Target","backgroundColor":"#999999","fontSize":10,"border":{"style":"blink"}}""");
		var fixture = new Fixture(source, target);

		await fixture.Service.PasteAsync(fixture.Service.Copy(source.Id), [target.Id]);

		var result = Data(target);
		Assert.Multiple(() =>
		{
			Assert.That(result.ContainsKey("backgroundColor"), Is.False);
			Assert.That(result.ContainsKey("fontSize"), Is.False);
			Assert.That(result.ContainsKey("border"), Is.False);
			Assert.That(result["label"]!.GetValue<string>(), Is.EqualTo("Target"));
		});
	}

	[Test]
	public void StateButton_CopiesTheShownState_FallingBackToTheBaseLook()
	{
		var source = Button(1,
			"""
			{"stateMode":true,"labelColor":"#eeeeee","fontFaceId":"font-a","textAlign":"right","iconColor":"#base00",
			"states":[
			{"id":"off","label":"Off","appearance":{"backgroundColor":"#111111"}},
			{"id":"on","label":"On","appearance":{"backgroundColor":"#222222","labelColor":"","fontFaceId":null,
			"iconColor":"#00ff00"}}]}
			""");
		var fixture = new Fixture(source);
		fixture.DerivedStates.GetAndSet(source.Id, "on");

		var values = JsonNode.Parse(fixture.Service.Copy(source.Id)!)!["values"]!.AsObject();

		Assert.Multiple(() =>
		{
			Assert.That(values["backgroundColor"]!.GetValue<string>(), Is.EqualTo("#222222"));
			Assert.That(values["labelColor"]!.GetValue<string>(), Is.EqualTo("#eeeeee"));
			Assert.That(values["fontFaceId"]!.GetValue<string>(), Is.EqualTo("font-a"));
			Assert.That(values["textAlign"]!.GetValue<string>(), Is.EqualTo("right"));
			Assert.That(values["iconColor"]!.GetValue<string>(), Is.EqualTo("#00ff00"));
		});
	}

	[Test]
	public async Task Paste_OnAStateButton_ReachesTheBaseAndEveryState()
	{
		var source = Button(1, StyledButton);
		var target = Button(2,
			"""
			{"stateMode":true,"backgroundColor":"#999999","states":[
			{"id":"off","label":"Off","appearance":{"label":"Muted","backgroundColor":"#ff0000","iconColor":"#aaaaaa"}},
			{"id":"on","label":"On","appearance":{"label":"Live","backgroundColor":"#00ff00"}}]}
			""");
		var fixture = new Fixture(source, target);

		await fixture.Service.PasteAsync(fixture.Service.Copy(source.Id), [target.Id]);

		var result = Data(target);
		var states = result["states"]!.AsArray().Select(state => state!["appearance"]!.AsObject()).ToList();
		Assert.Multiple(() =>
		{
			Assert.That(result["backgroundColor"]!.GetValue<string>(), Is.EqualTo("#112233"));
			Assert.That(result.ContainsKey("iconColor"), Is.False, "a state button keeps its icon look on the states");
			Assert.That(states.Select(state => state["backgroundColor"]!.GetValue<string>()),
				Is.EqualTo(new[] { "#112233", "#112233" }));
			Assert.That(states.Select(state => state["iconColor"]!.GetValue<string>()),
				Is.EqualTo(new[] { "#123456", "#123456" }));
			Assert.That(states.Select(state => state["fontSize"]!.GetValue<int>()), Is.EqualTo(new[] { 20, 20 }));
			Assert.That(states.Select(state => state["label"]!.GetValue<string>()),
				Is.EqualTo(new[] { "Muted", "Live" }));
		});
	}

	[Test]
	public async Task Paste_OnAProviderBoundButton_AlsoRestylesItsBackedUpManualStates()
	{
		var source = Button(1, StyledButton);
		var target = Button(2,
			"""
			{"stateMode":true,
			"stateProvider":{"blockId":"blk-1","integrationId":"spotify","actionId":"now-playing",
			"states":[{"id":"playing","label":"Playing"}]},
			"states":[{"id":"playing","label":"Playing","appearance":{"backgroundColor":"#ff0000"}}],
			"manualStateBackup":{"states":[{"id":"mine","label":"Mine","appearance":{"backgroundColor":"#0000ff"}}]}}
			""");
		var fixture = new Fixture(source, target);

		await fixture.Service.PasteAsync(fixture.Service.Copy(source.Id), [target.Id]);

		var backup = Data(target)["manualStateBackup"]!["states"]![0]!["appearance"]!;
		Assert.That(backup["backgroundColor"]!.GetValue<string>(), Is.EqualTo("#112233"));
	}

	[Test]
	public async Task Paste_OnAButtonWithALegacyStateIcon_KeepsTheIconAndFramesIt()
	{
		var source = Button(1, StyledButton);
		var target = Button(2,
			"""{"label":"Old","states":[{"id":"off","label":"Off","appearance":{"iconId":"icon-old","iconColor":"#aaaaaa"}}]}""");
		var fixture = new Fixture(source, target);

		await fixture.Service.PasteAsync(fixture.Service.Copy(source.Id), [target.Id]);

		var result = Data(target);
		Assert.Multiple(() =>
		{
			Assert.That(result["icon"]!["reference"]!.GetValue<string>(), Is.EqualTo("icon-old"));
			Assert.That(result["iconColor"]!.GetValue<string>(), Is.EqualTo("#123456"));
			Assert.That(result["iconDisplay"]!["zoom"]!.GetValue<int>(), Is.EqualTo(150));
		});
	}

	[Test]
	public void Copy_FromAButtonWithALegacyStateIcon_ReadsItsIconLook()
	{
		var source = Button(1,
			"""
			{"label":"Old","states":[{"id":"off","label":"Off",
			"appearance":{"iconId":"icon-old","iconColor":"#aaaaaa","iconDisplay":{"zoom":80}}}]}
			""");
		var fixture = new Fixture(source);

		var values = JsonNode.Parse(fixture.Service.Copy(source.Id)!)!["values"]!.AsObject();

		Assert.Multiple(() =>
		{
			Assert.That(values["iconColor"]!.GetValue<string>(), Is.EqualTo("#aaaaaa"));
			Assert.That(values["iconDisplay"]!["zoom"]!.GetValue<int>(), Is.EqualTo(80));
		});
	}

	[Test]
	public async Task ButtonToSlider_WritesOnlyThePropertiesBothHave()
	{
		var source = Button(1, StyledButton);
		var target = Widget(2,
			WidgetTypeIds.Slider,
			"""
			{"orientation":"horizontal","label":"Volume","color":"#00ff00","labelColor":"#000000",
			"backgroundColor":"#999999","valueVariable":"volume","thresholdsEnabled":true}
			""");
		var fixture = new Fixture(source, target);

		var outcome = await fixture.Service.PasteAsync(fixture.Service.Copy(source.Id), [target.Id]);

		var result = Data(target);
		Assert.Multiple(() =>
		{
			Assert.That(outcome, Is.EqualTo(WidgetStylePasteOutcome.Applied));
			Assert.That(result["backgroundColor"]!.GetValue<string>(), Is.EqualTo("#112233"));
			Assert.That(result["labelColor"]!.GetValue<string>(), Is.EqualTo("#fefefe"));
			Assert.That(result["iconColor"]!.GetValue<string>(), Is.EqualTo("#123456"));
			Assert.That(result["border"]!["color"]!.GetValue<string>(), Is.EqualTo("#abcdef"));
			Assert.That(result["color"]!.GetValue<string>(), Is.EqualTo("#00ff00"));
			Assert.That(result["label"]!.GetValue<string>(), Is.EqualTo("Volume"));
			Assert.That(result["valueVariable"]!.GetValue<string>(), Is.EqualTo("volume"));
			Assert.That(result["thresholdsEnabled"]!.GetValue<bool>(), Is.True);
			Assert.That(result.ContainsKey("fontSize"), Is.False);
			Assert.That(result.ContainsKey("iconDisplay"), Is.False);
		});
	}

	[Test]
	public async Task BatteryToBattery_CarriesTheColourMode_ButNotTheThresholds()
	{
		var source = Widget(1,
			WidgetTypeIds.Battery,
			"""{"color":"#ff00ff","colorMode":"single","chargingColor":false,"thresholds":{"steps":[]}}""");
		var target = Widget(2, WidgetTypeIds.Battery, """{"device":"mouse","showName":true}""");
		var fixture = new Fixture(source, target);

		await fixture.Service.PasteAsync(fixture.Service.Copy(source.Id), [target.Id]);

		var result = Data(target);
		Assert.Multiple(() =>
		{
			Assert.That(result["color"]!.GetValue<string>(), Is.EqualTo("#ff00ff"));
			Assert.That(result["colorMode"]!.GetValue<string>(), Is.EqualTo("single"));
			Assert.That(result["chargingColor"]!.GetValue<bool>(), Is.False);
			Assert.That(result["device"]!.GetValue<string>(), Is.EqualTo("mouse"));
			Assert.That(result.ContainsKey("thresholds"), Is.False);
		});
	}

	[Test]
	public async Task ProviderType_DeclaringABackground_TakesBackgroundAndBorder()
	{
		var registry = new WidgetTypeRegistry(new RecordingMediator());
		var registration = await registry.Register("com.example.frames",
			new WidgetTypeDescriptor("frame", "Frame")
			{
				AppearanceProperties = [WidgetAppearanceProperty.BackgroundColor]
			});
		var source = Button(1, StyledButton);
		var target = Widget(2, registration.WidgetTypeId, """{"photo":"a.png"}""");
		var fixture = new Fixture(registry, source, target);

		await fixture.Service.PasteAsync(fixture.Service.Copy(source.Id), [target.Id]);

		Assert.That(Json(Data(target)),
			Is.EqualTo(
				"""{"photo":"a.png","backgroundColor":"#112233","border":{"style":"static","color":"#abcdef"}}"""));
	}

	[Test]
	public async Task ProviderType_NotDeclaringABackground_TakesTheBorder_AndKeepsItsOwnBackgroundKey()
	{
		var registry = new WidgetTypeRegistry(new RecordingMediator());
		var registration = await registry.Register("com.example.frames", new WidgetTypeDescriptor("frame", "Frame"));
		var styled = Button(1, StyledButton);
		var plain = Button(2, """{"label":"Plain"}""");
		var target = Widget(3, registration.WidgetTypeId, """{"backgroundColor":"plugin-owned"}""");
		var fixture = new Fixture(registry, styled, plain, target);

		await fixture.Service.PasteAsync(fixture.Service.Copy(styled.Id), [target.Id]);
		var afterStyled = Json(Data(target));
		await fixture.Service.PasteAsync(fixture.Service.Copy(plain.Id), [target.Id]);

		Assert.Multiple(() =>
		{
			Assert.That(afterStyled,
				Is.EqualTo("""{"backgroundColor":"plugin-owned","border":{"style":"static","color":"#abcdef"}}"""));
			Assert.That(Json(Data(target)), Is.EqualTo("""{"backgroundColor":"plugin-owned"}"""));
		});
	}

	[Test]
	public async Task ProviderType_AsTheSource_OffersOnlyWhatItMayReceive()
	{
		var registry = new WidgetTypeRegistry(new RecordingMediator());
		var registration = await registry.Register("com.example.frames", new WidgetTypeDescriptor("frame", "Frame"));
		var source = Widget(1,
			registration.WidgetTypeId,
			"""{"backgroundColor":"plugin-owned","border":{"style":"blink"}}""");
		var target = Button(2, """{"label":"Target","backgroundColor":"#999999"}""");
		var fixture = new Fixture(registry, source, target);

		await fixture.Service.PasteAsync(fixture.Service.Copy(source.Id), [target.Id]);

		var result = Data(target);
		Assert.Multiple(() =>
		{
			Assert.That(result["backgroundColor"]!.GetValue<string>(), Is.EqualTo("#999999"));
			Assert.That(result["border"]!["style"]!.GetValue<string>(), Is.EqualTo("blink"));
		});
	}

	[Test]
	public async Task ProviderType_WithAClosedSchema_StillTakesTheBorder_AndDoesNotBlockTheOtherTargets()
	{
		var registry = new WidgetTypeRegistry(new RecordingMediator());
		var registration = await registry.Register("com.example.frames",
			new WidgetTypeDescriptor("frame",
				"Frame",
				null,
				null,
				"""{"type":"object","additionalProperties":false}""")
			{
				AppearanceProperties = [WidgetAppearanceProperty.BackgroundColor]
			});
		var source = Button(1, StyledButton);
		var frame = Widget(2, registration.WidgetTypeId, "{}");
		var other = Button(3, """{"label":"Other"}""");
		var fixture = new Fixture(registry, source, frame, other);

		var outcome = await fixture.Service.PasteAsync(fixture.Service.Copy(source.Id), [frame.Id, other.Id]);

		Assert.Multiple(() =>
		{
			Assert.That(outcome, Is.EqualTo(WidgetStylePasteOutcome.Applied));
			Assert.That(Json(Data(frame)), Is.EqualTo("""{"border":{"style":"static","color":"#abcdef"}}"""));
			Assert.That(Data(other)["backgroundColor"]!.GetValue<string>(), Is.EqualTo("#112233"));
		});
	}

	[Test]
	public async Task Paste_WithoutAnIconLook_LeavesTheIconsOfDormantStatesAlone()
	{
		var clock = Widget(1, WidgetTypeIds.Clock, """{"backgroundColor":"#101010","textColor":"#fafafa"}""");
		var target = Button(2,
			"""
			{"label":"Single","icon":{"type":"pack","reference":"pack/root"},"states":[
			{"id":"off","label":"Off","appearance":{"icon":{"type":"pack","reference":"pack/off"},"iconColor":"#aaaaaa"}},
			{"id":"on","label":"On","appearance":{"icon":{"type":"pack","reference":"pack/on"}}}]}
			""");
		var fixture = new Fixture(clock, target);

		await fixture.Service.PasteAsync(fixture.Service.Copy(clock.Id), [target.Id]);

		var result = Data(target);
		var off = result["states"]![0]!["appearance"]!;
		Assert.Multiple(() =>
		{
			Assert.That(result["backgroundColor"]!.GetValue<string>(), Is.EqualTo("#101010"));
			Assert.That(off["icon"]!["reference"]!.GetValue<string>(), Is.EqualTo("pack/off"));
			Assert.That(off["iconColor"]!.GetValue<string>(), Is.EqualTo("#aaaaaa"));
			Assert.That(result["icon"]!["reference"]!.GetValue<string>(), Is.EqualTo("pack/root"));
		});
	}

	[Test]
	public async Task TargetTheSchemaRejects_LeavesEveryTargetUnwritten()
	{
		var clock = Widget(1, WidgetTypeIds.Clock, """{"showSeconds":true}""");
		var button = Button(2, """{"label":"Target"}""");
		var fixture = new Fixture(clock, button);

		var outcome = await fixture.Service.PasteAsync(
			"""{"keys":["backgroundColor","fontSize"],"values":{"backgroundColor":"#111111","fontSize":"big"}}""",
			[clock.Id, button.Id]);

		Assert.Multiple(() =>
		{
			Assert.That(outcome, Is.EqualTo(WidgetStylePasteOutcome.Rejected));
			Assert.That(fixture.Widgets.Updated, Is.Empty);
			Assert.That(clock.Data, Is.EqualTo("""{"showSeconds":true}"""));
			Assert.That(button.Data, Is.EqualTo("""{"label":"Target"}"""));
		});
	}

	[Test]
	public async Task MissingTarget_LeavesEveryTargetUnwritten()
	{
		var source = Button(1, StyledButton);
		var target = Button(2, """{"label":"Target"}""");
		var fixture = new Fixture(source, target);

		var outcome = await fixture.Service.PasteAsync(fixture.Service.Copy(source.Id), [target.Id, Id(9)]);

		Assert.Multiple(() =>
		{
			Assert.That(outcome, Is.EqualTo(WidgetStylePasteOutcome.WidgetNotFound));
			Assert.That(fixture.Widgets.Updated, Is.Empty);
			Assert.That(target.Data, Is.EqualTo("""{"label":"Target"}"""));
		});
	}

	[TestCase("")]
	[TestCase("not json")]
	[TestCase("""{"keys":[],"values":{}}""")]
	[TestCase("""{"keys":["backgroundColor"]}""")]
	[TestCase("""{"keys":["backgroundColor"],"values":{"labelColor":"#111111"}}""")]
	[TestCase("""{"keys":["border"],"values":{"border":"static"}}""")]
	[TestCase("""{"keys":["backgroundColor"],"values":{"backgroundColor":{"nested":true}}}""")]
	public async Task MalformedStyle_IsRefused(string style)
	{
		var target = Button(1, """{"label":"Target"}""");
		var fixture = new Fixture(target);

		var outcome = await fixture.Service.PasteAsync(style, [target.Id]);

		Assert.Multiple(() =>
		{
			Assert.That(outcome, Is.EqualTo(WidgetStylePasteOutcome.InvalidStyle));
			Assert.That(fixture.Widgets.Updated, Is.Empty);
		});
	}

	[Test]
	public async Task RepeatedTarget_IsWrittenOnce()
	{
		var source = Button(1, StyledButton);
		var target = Button(2, """{"label":"Target"}""");
		var fixture = new Fixture(source, target);

		var paste = fixture.Service.PasteAsync(fixture.Service.Copy(source.Id), [target.Id, target.Id]);
		var finished = await Task.WhenAny(paste, Task.Delay(TimeSpan.FromSeconds(10)));

		Assert.Multiple(() =>
		{
			Assert.That(finished, Is.SameAs(paste), "a repeated id must not wait on its own lock");
			Assert.That(fixture.Widgets.Updated, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task FailedWrite_IsReported()
	{
		var source = Button(1, StyledButton);
		var target = Button(2, """{"label":"Target"}""");
		var fixture = new Fixture(source, target);
		fixture.Widgets.FailWrites = true;

		var outcome = await fixture.Service.PasteAsync(fixture.Service.Copy(source.Id), [target.Id]);

		Assert.That(outcome, Is.EqualTo(WidgetStylePasteOutcome.WriteFailed));
	}

	[Test]
	public void UnknownWidget_HasNoStyleToCopy()
	{
		var fixture = new Fixture(Button(1, StyledButton));

		Assert.That(fixture.Service.Copy(Id(9)), Is.Null);
	}

	[Test]
	public void EveryBuiltInType_ListsOnlyPropertiesItsSchemaKnows()
	{
		var schemas = new WidgetDataSchemaProvider(new WidgetTypeRegistry(new RecordingMediator())).All();

		Assert.Multiple(() =>
		{
			foreach (var type in WidgetTypeIds.BuiltIn)
			{
				var keys = WidgetStyleJson.BuiltInKeys(type);
				Assert.That(keys, Is.Not.Null.And.Not.Empty, type);
				var properties = schemas[type].GetProperty("properties");
				foreach (var key in keys!)
				{
					Assert.That(properties.TryGetProperty(key, out _), Is.True, $"{type}.{key}");
				}
			}
		});
	}

	private static Guid Id(int number)
	{
		return new Guid(number, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
	}

	private static WidgetEntity Button(int number, string data)
	{
		return Widget(number, WidgetTypeIds.ActionButton, data);
	}

	private static WidgetEntity Widget(int number, string type, string data)
	{
		return new WidgetEntity { Id = Id(number), Type = type, Data = Json(JsonNode.Parse(data)) };
	}

	private static JsonObject Data(WidgetEntity widget)
	{
		return JsonNode.Parse(widget.Data!)!.AsObject();
	}

	private static JsonObject Normalized(string? data)
	{
		return ActionButtonStateJson.Normalize(JsonNode.Parse(data!)!.AsObject());
	}

	private static string? Json(JsonNode? node)
	{
		return node?.ToJsonString();
	}

	private sealed class Fixture
	{
		public Fixture(params WidgetEntity[] widgets)
			: this(new WidgetTypeRegistry(new RecordingMediator()), widgets)
		{
		}

		public Fixture(WidgetTypeRegistry registry, params WidgetEntity[] widgets)
		{
			var folder = new FolderEntity
			{
				Id = Guid.NewGuid(),
				ProfileId = Guid.NewGuid(),
				Name = "Home",
				Order = 0,
				Widgets = [.. widgets]
			};

			Widgets = new RecordingWidgetService();
			DerivedStates = new WidgetDerivedStateStore();
			Service = new WidgetStyleService(new SingleFolderCache(folder),
				Widgets,
				new WidgetDataWriteLock(),
				DerivedStates,
				registry,
				new WidgetDataSchemaProvider(registry),
				new WidgetAppearanceSchemaProbe(),
				new LoggerConfiguration().CreateLogger());
		}

		public RecordingWidgetService Widgets { get; }
		public WidgetDerivedStateStore DerivedStates { get; }
		public WidgetStyleService Service { get; }
	}

	private sealed class RecordingWidgetService : IWidgetService
	{
		public List<WidgetEntity> Updated { get; } = [];

		public bool FailWrites { get; set; }

		public Task<Result<WidgetEntity, WidgetError>> Update(WidgetEntity widget)
		{
			if (FailWrites)
			{
				return Task.FromResult(Result.Fail<WidgetEntity, WidgetError>(WidgetError.NotFound));
			}

			Updated.Add(widget);
			return Task.FromResult(Result.Ok<WidgetEntity, WidgetError>(widget));
		}

		public Task<Result<WidgetEntity, WidgetError>> Create(
			Guid folderId,
			WidgetEntity widget,
			Guid? sourceWidgetId = null)
		{
			throw new NotSupportedException();
		}

		public Task<Result<List<WidgetEntity>, WidgetError>> UpdatePositions(
			Guid folderId,
			IReadOnlyList<WidgetPlacement> placements)
		{
			throw new NotSupportedException();
		}

		public Task<Result<WidgetError>> Delete(Guid widgetId, Guid folderId)
		{
			throw new NotSupportedException();
		}

		public Task<Result<WidgetEntity, WidgetError>> SetPinned(
			Guid folderId,
			Guid widgetId,
			bool pinned,
			PinScope? scope = null)
		{
			throw new NotSupportedException();
		}

		public Task<Result<List<WidgetEntity>, WidgetError>> CreateMany(
			Guid folderId,
			IReadOnlyList<WidgetEntity> widgets,
			IReadOnlyList<Guid>? replaceIds = null,
			IReadOnlyList<Guid?>? sourceWidgetIds = null)
		{
			throw new NotSupportedException();
		}

		public Task<Result<WidgetError>> DeleteMany(Guid folderId, IReadOnlyList<Guid> widgetIds)
		{
			throw new NotSupportedException();
		}

		public Task<Result<List<WidgetEntity>, WidgetError>> SetPinnedMany(
			Guid folderId,
			IReadOnlyList<Guid> widgetIds,
			bool pinned,
			PinScope? scope = null)
		{
			throw new NotSupportedException();
		}
	}

	private sealed class SingleFolderCache : IFolderCache
	{
		private readonly FolderEntity _folder;

		public SingleFolderCache(FolderEntity folder)
		{
			_folder = folder;
		}

		public List<FolderEntity> GetAllFolders()
		{
			return [_folder];
		}

		public FolderEntity? GetFolderById(Guid id)
		{
			return id == _folder.Id ? _folder : null;
		}

		public Task InitializeCache()
		{
			return Task.CompletedTask;
		}

		public List<FolderEntity> GetFoldersByParentId(Guid? parentId)
		{
			return [_folder];
		}

		public List<FolderEntity> GetFoldersByProfileId(Guid profileId)
		{
			return [_folder];
		}

		public Task AddOrUpdate(FolderEntity folder)
		{
			return Task.CompletedTask;
		}

		public Task AddOrUpdateRange(IReadOnlyCollection<FolderEntity> folders)
		{
			return Task.CompletedTask;
		}

		public Task<FolderSubtreeRemoval> RemoveSubtree(Guid rootId)
		{
			return Task.FromResult(new FolderSubtreeRemoval(false, [], []));
		}

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
