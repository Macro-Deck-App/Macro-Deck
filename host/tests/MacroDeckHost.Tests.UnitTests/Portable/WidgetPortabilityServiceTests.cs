using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeckHost.Application.Portable;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Icons;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Infrastructure.Portable;
using MacroDeckHost.Widgets.ActionButton;
using MacroDeckHost.Widgets.Slider;

namespace MacroDeckHost.Tests.UnitTests.Portable;

[TestFixture]
public class WidgetPortabilityServiceTests
{
	private PortabilityTestHarness _harness = null!;

	[SetUp]
	public void SetUp() => _harness = new PortabilityTestHarness();

	[TearDown]
	public void TearDown() => _harness.Dispose();

	[Test]
	public async Task Export_ThenImport_IntoAnotherFolder_RecreatesTheWidget_ReusingTheIcon()
	{
		var pack = await _harness.Icons.CreatePack("Source Pack");
		var icon = await _harness.AddReadyIcon(pack.Id, "star", sizes: [128]);
		var (profile, sourceFolder, widget) = await _harness.SeedProfile(
			$"{{\"states\":{{\"off\":{{\"iconId\":\"{icon.Id}\"}}}}}}");
		var target = await AddFolder(profile.Id, "Target");

		var export = await _harness.WidgetService.Export(sourceFolder.Id,
			[widget.Id],
			PortableExportOptions.Default,
			CancellationToken.None);
		Assert.That(export.Success, Is.True);

		var import = await _harness.WidgetService.Import(target.Id,
			anchorX: 2,
			anchorY: 1,
			export.Data!,
			password: null,
			CancellationToken.None);

		Assert.That(import.Success, Is.True);
		var created = import.Data!.Single();
		var referencedIcons = GuidReferences.ExtractAll(created.Data)
			.Where(id => _harness.Icons.Cache.GetIconById(id) is not null)
			.ToList();

		Assert.Multiple(() =>
		{
			Assert.That(created.Id, Is.Not.EqualTo(widget.Id));
			Assert.That(created.FolderId, Is.EqualTo(target.Id));
			Assert.That(created.PositionX, Is.EqualTo(2));
			Assert.That(created.PositionY, Is.EqualTo(1));
			Assert.That(referencedIcons, Has.Count.EqualTo(1));
			Assert.That(referencedIcons[0], Is.EqualTo(icon.Id));
			Assert.That(_harness.FolderCache.GetFolderById(target.Id)!.Widgets, Has.Count.EqualTo(1));
		});
	}

	// Acceptance Group I, scenario 32 (asset-collection half; the provider-active leg is the next test
	// below). An asset collector that walks fields literally named iconId would collect nothing once the
	// reference moves to icon.reference and silently produce blank buttons; GuidReferences.ExtractAll
	// is a blind regex over the raw JSON, so it must keep finding a GUID nested under icon.reference with
	// no code change of its own.
	[Test]
	public async Task
		Export_StillCollectsIconPackAssetsNestedUnderTheNewIconField_AndLeavesANonIconPackReferenceUntouched()
	{
		var pack = await _harness.Icons.CreatePack("Source Pack");
		var iconA = await _harness.AddReadyIcon(pack.Id, "icon-a", sizes: [128]);
		var iconB = await _harness.AddReadyIcon(pack.Id, "icon-b", sizes: [128]);

		var (profile, sourceFolder, button) = await _harness.SeedProfile($$"""
																		   {"stateMode":true,"states":[
																		   {"id":"on","label":"On","appearance":{"icon":{"type":"icon-pack","reference":"{{iconA.Id}}"} } },
																		   {"id":"off","label":"Off","appearance":{"icon":{"type":"icon-pack","reference":"{{iconB.Id}}"} } }]}
																		   """);
		const string sliderData
			= """{"icon":{"type":"plugin-asset","reference":"spotify:album/4aawyAB9vmqN3uQ7FjRGTy"}}""";
		var slider = new WidgetEntity
		{
			Id = Guid.NewGuid(),
			FolderId = sourceFolder.Id,
			Type = WidgetTypeIds.Slider,
			PositionX = 1,
			PositionY = 0,
			Width = 1,
			Height = 1,
			Data = sliderData
		};
		_harness.FolderCache.AddWidget(sourceFolder.Id, slider);

		var target = await AddFolder(profile.Id, "Target");

		var export = await _harness.WidgetService.Export(sourceFolder.Id,
			[button.Id, slider.Id],
			PortableExportOptions.Default,
			CancellationToken.None);
		Assert.That(export.Success, Is.True);

		var archive = PortableArchive.Read(export.Data!, password: null);
		Assert.Multiple(() =>
		{
			Assert.That(archive.Status, Is.EqualTo(PortableReadStatus.Success));
			Assert.That(archive.Content!.Icons.Select(i => i.Id),
				Is.EquivalentTo(new[] { iconA.Id, iconB.Id }),
				"the archive contains the assets for both icon-pack icons and no phantom entry for the plugin-asset reference");
		});

		var import = await _harness.WidgetService.Import(target.Id,
			0,
			0,
			export.Data!,
			password: null,
			CancellationToken.None);
		Assert.That(import.Success, Is.True);

		var importedButton = import.Data!.Single(w => w.Type == WidgetTypeIds.ActionButton);
		var importedSlider = import.Data!.Single(w => w.Type == WidgetTypeIds.Slider);
		var config = ActionButtonWidgetData.Parse(JsonDocument.Parse(importedButton.Data!).RootElement);
		var onIcon = config.ResolveIcon("on")!.Icon;
		var offIcon = config.ResolveIcon("off")!.Icon;
		var onIconEntity = Guid.TryParse(onIcon.Reference, out var onIconId)
			? _harness.Icons.Cache.GetIconById(onIconId)
			: null;
		var offIconEntity = Guid.TryParse(offIcon.Reference, out var offIconId)
			? _harness.Icons.Cache.GetIconById(offIconId)
			: null;

		Assert.Multiple(() =>
		{
			Assert.That(onIcon.Type, Is.EqualTo(WidgetIconReference.IconPackType));
			Assert.That(onIconEntity, Is.Not.Null);
			Assert.That(onIconEntity!.MasterContentHash,
				Is.EqualTo(iconA.MasterContentHash),
				"the button renders ICON_A");
			Assert.That(offIcon.Type, Is.EqualTo(WidgetIconReference.IconPackType));
			Assert.That(offIconEntity, Is.Not.Null);
			Assert.That(offIconEntity!.MasterContentHash,
				Is.EqualTo(iconB.MasterContentHash),
				"the button renders ICON_B");
			Assert.That(importedSlider.Data, Is.EqualTo(sliderData), "the Slider's icon object is character-identical");
		});
	}

	// Acceptance Group I, scenario 32 (the provider-active export leg, issue #425 - deferred from the
	// asset-collection test above since it needed the icon-provider work package): exporting a button while
	// its icon provider is active still collects its per-state icon-pack assets exactly as an unprovided
	// button would, and the provider assignment travelling along in the JSON is inert to portability - it
	// does not create a phantom asset entry and does not stop the manual per-state icons from surviving
	// the round trip. Imported where the providing plugin is not installed, the button renders its
	// original per-state icons untouched.
	[Test]
	public async Task
		Export_OfAButtonWithAnActiveIconProvider_StillCollectsItsPerStateIconsAndImportsRenderingThemUnchanged()
	{
		var pack = await _harness.Icons.CreatePack("Source Pack");
		var iconA = await _harness.AddReadyIcon(pack.Id, "icon-a", sizes: [128]);
		var iconB = await _harness.AddReadyIcon(pack.Id, "icon-b", sizes: [128]);

		var (profile, sourceFolder, button) = await _harness.SeedProfile($$"""
																		   {"stateMode":true,"states":[
																		   {"id":"on","label":"On","appearance":{"icon":{"type":"icon-pack","reference":"{{iconA.Id}}"} } },
																		   {"id":"off","label":"Off","appearance":{"icon":{"type":"icon-pack","reference":"{{iconB.Id}}"} } }],
																		   "iconProvider":{"blockId":"blk-1","integrationId":"spotify","actionId":"current-track"},
																		   "flows":"[{\"triggerId\":\"t\",\"triggerType\":\"onShortPress\",\"children\":[{\"id\":\"blk-1\",\"type\":\"action\",\"blockType\":\"spotify.current-track\",\"integrationId\":\"spotify\",\"actionId\":\"current-track\",\"parameters\":[]}]}]"}
																		   """);
		var target = await AddFolder(profile.Id, "Target");

		var export = await _harness.WidgetService.Export(sourceFolder.Id,
			[button.Id],
			PortableExportOptions.Default,
			CancellationToken.None);
		Assert.That(export.Success, Is.True);

		var archive = PortableArchive.Read(export.Data!, password: null);
		Assert.Multiple(() =>
		{
			Assert.That(archive.Status, Is.EqualTo(PortableReadStatus.Success));
			Assert.That(archive.Content!.Icons.Select(i => i.Id),
				Is.EquivalentTo(new[] { iconA.Id, iconB.Id }),
				"the archive contains the assets for both per-state icons and no phantom entry for the provider");
		});

		var import = await _harness.WidgetService.Import(target.Id,
			0,
			0,
			export.Data!,
			password: null,
			CancellationToken.None);
		Assert.That(import.Success, Is.True);

		var importedButton = import.Data!.Single();
		var config = ActionButtonWidgetData.Parse(JsonDocument.Parse(importedButton.Data!).RootElement);
		var onIcon = config.ResolveIcon("on")!.Icon;
		var offIcon = config.ResolveIcon("off")!.Icon;
		var onIconEntity = Guid.TryParse(onIcon.Reference, out var onIconId)
			? _harness.Icons.Cache.GetIconById(onIconId)
			: null;
		var offIconEntity = Guid.TryParse(offIcon.Reference, out var offIconId)
			? _harness.Icons.Cache.GetIconById(offIconId)
			: null;

		Assert.Multiple(() =>
		{
			Assert.That(onIconEntity, Is.Not.Null);
			Assert.That(onIconEntity!.MasterContentHash,
				Is.EqualTo(iconA.MasterContentHash),
				"renders ICON_A, not the provider's ICON_C - nothing here ever resolves a live provider");
			Assert.That(offIconEntity, Is.Not.Null);
			Assert.That(offIconEntity!.MasterContentHash, Is.EqualTo(iconB.MasterContentHash), "renders ICON_B");
		});
	}

	// Acceptance Group I, scenario 33: a legacy iconId at all three storage sites (an Action Button's flat
	// root, a per-state appearance, and a Slider's root) imports and renders identically from an archive
	// that predates this migration entirely - catching a migration wired into widget load but not archive
	// import.
	[Test]
	public async Task Import_AnArchiveProducedByAnOlderBuild_MigratesLegacyIconIdAtAllThreeSitesAndRendersIdentically()
	{
		var profile = await _harness.AddProfile("Target");
		var folder = await _harness.AddFolder(profile.Id, "Home");

		var flatIconId = Guid.NewGuid();
		var stateIconId = Guid.NewGuid();
		var sliderIconId = Guid.NewGuid();
		var masterBytesByIcon = new Dictionary<Guid, byte[]>
		{
			[flatIconId] = "flat-icon-bytes"u8.ToArray(),
			[stateIconId] = "state-icon-bytes"u8.ToArray(),
			[sliderIconId] = "slider-icon-bytes"u8.ToArray()
		};

		var content = new PortableContent
		{
			Kind = PortableArchiveKind.Widgets,
			Widgets =
			[
				new PortableWidget
				{
					Type = WidgetTypeIds.ActionButton,
					Data = $$"""{"stateMode":false,"iconId":"{{flatIconId}}","label":"Play"}"""
				},
				new PortableWidget
				{
					Type = WidgetTypeIds.ActionButton,
					PositionX = 1,
					Data = $$"""
							 {"stateMode":true,"states":[{"id":"on","label":"On","appearance":{"iconId":"{{stateIconId}}"} },
							 {"id":"off","label":"Off","appearance":{} }]}
							 """
				},
				new PortableWidget
				{
					Type = WidgetTypeIds.Slider,
					PositionX = 2,
					Data = $$"""{"iconId":"{{sliderIconId}}","label":"Volume"}"""
				}
			],
			Icons = masterBytesByIcon.Keys.Select(id => new PortableIcon { Id = id, Name = id.ToString() }).ToList()
		};
		var iconFiles = masterBytesByIcon.Select(pair => new PortableIconFile(pair.Key, "master", pair.Value)).ToList();
		var archiveBytes = PortableArchive.Write(new PortableArchiveManifest { Kind = PortableArchiveKind.Widgets },
			content,
			iconFiles,
			password: null);

		var import = await _harness.WidgetService.Import(folder.Id,
			0,
			0,
			archiveBytes,
			password: null,
			CancellationToken.None);

		Assert.That(import.Success, Is.True);

		var created = import.Data!;
		var actionButtons = created.Where(w => w.Type == WidgetTypeIds.ActionButton).ToList();
		var flat = actionButtons.Single(w => !w.Data!.Contains("\"states\""));
		var stateful = actionButtons.Single(w => w.Data!.Contains("\"states\""));
		var importedSlider = created.Single(w => w.Type == WidgetTypeIds.Slider);

		var flatData = JsonNode.Parse(flat.Data!)!.AsObject();
		var flatIcon = ActionButtonWidgetData.Parse(JsonDocument.Parse(flat.Data!).RootElement).ResolveIcon(null)!.Icon;
		var onIcon = ActionButtonWidgetData.Parse(JsonDocument.Parse(stateful.Data!).RootElement).ResolveIcon("on")!
			.Icon;
		var sliderIcon = SliderWidgetData.Parse(JsonDocument.Parse(importedSlider.Data!).RootElement).Icon!.Value;

		Assert.Multiple(() =>
		{
			Assert.That(flatData.ContainsKey("iconId"), Is.False, "the legacy key must not survive the import");
			Assert.That(flatIcon.Type, Is.EqualTo(WidgetIconReference.IconPackType));
			Assert.That(RenderedMasterHash(flatIcon),
				Is.EqualTo(MasterContentHash.Compute(masterBytesByIcon[flatIconId]).Value));

			Assert.That(onIcon.Type, Is.EqualTo(WidgetIconReference.IconPackType));
			Assert.That(RenderedMasterHash(onIcon),
				Is.EqualTo(MasterContentHash.Compute(masterBytesByIcon[stateIconId]).Value));

			Assert.That(sliderIcon.Type, Is.EqualTo(WidgetIconReference.IconPackType));
			Assert.That(RenderedMasterHash(sliderIcon),
				Is.EqualTo(MasterContentHash.Compute(masterBytesByIcon[sliderIconId]).Value));
		});
	}

	private string? RenderedMasterHash(WidgetIconReference reference)
		=> Guid.TryParse(reference.Reference, out var id)
			? _harness.Icons.Cache.GetIconById(id)?.MasterContentHash
			: null;

	[Test]
	public async Task Import_WhenFolderIsFull_ReturnsValidationError()
	{
		var (profile, sourceFolder, widget) = await _harness.SeedProfile("{\"label\":\"x\"}");
		var target = await AddFolder(profile.Id, "Tiny", rows: 1, columns: 1);
		_harness.AddWidget(target.Id);

		var export = await _harness.WidgetService.Export(sourceFolder.Id,
			[widget.Id],
			PortableExportOptions.Default,
			CancellationToken.None);
		var import = await _harness.WidgetService.Import(target.Id,
			0,
			0,
			export.Data!,
			password: null,
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(import.Success, Is.False);
			Assert.That(import.Error, Is.EqualTo(PortabilityError.ValidationError));
		});
	}

	[Test]
	public async Task G3_G4_Import_RespectsSubtreeReach_AndCreatesAnUnpinnedWidget()
	{
		var profile = await _harness.AddProfile("Source");
		var main = await _harness.AddFolder(profile.Id, "Main", rows: 4, columns: 4);
		var games = await _harness.AddFolder(profile.Id, "Games", main.Id, rows: 4, columns: 4);
		var retro = await _harness.AddFolder(profile.Id, "Retro", games.Id, rows: 1, columns: 1);
		var work = await _harness.AddFolder(profile.Id, "Work", rows: 4, columns: 4);
		var mail = await _harness.AddFolder(profile.Id, "Mail", work.Id, rows: 4, columns: 4);

		var pin = new WidgetEntity
		{
			Id = Guid.NewGuid(),
			FolderId = main.Id,
			Type = WidgetTypeIds.ActionButton,
			PositionX = 0,
			PositionY = 0,
			Width = 1,
			Height = 1,
			IsPinned = true,
			PinScope = PinScope.Subtree
		};
		_harness.FolderCache.AddWidget(main.Id, pin);

		var source = await _harness.AddFolder(profile.Id, "ArchiveSource", rows: 4, columns: 4);
		var archiveWidget = _harness.AddWidget(source.Id, x: 0, y: 0);
		var export = await _harness.WidgetService.Export(source.Id,
			[archiveWidget.Id],
			PortableExportOptions.Default,
			CancellationToken.None);
		Assert.That(export.Success, Is.True);

		var importMail =
			await _harness.WidgetService.Import(mail.Id, 0, 0, export.Data!, password: null, CancellationToken.None);
		var importRetro = await _harness.WidgetService.Import(retro.Id,
			0,
			0,
			export.Data!,
			password: null,
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(importMail.Success, Is.True, "Mail is outside the pin's subtree reach");
			Assert.That(importRetro.Success, Is.False, "Retro is inside the pin's subtree reach");
			Assert.That(importRetro.Error, Is.EqualTo(PortabilityError.ValidationError));
			Assert.That(_harness.FolderCache.GetFolderById(retro.Id)!.Widgets, Is.Empty);
		});
		var createdInMail = importMail.Data!.Single();
		Assert.Multiple(() =>
		{
			Assert.That(createdInMail.IsPinned, Is.False);
			Assert.That(createdInMail.PinScope, Is.EqualTo(PinScope.Profile));
		});
	}

	[Test]
	public async Task Export_UnknownWidget_ReturnsValidationError()
	{
		var (_, folder, _) = await _harness.SeedProfile("{\"label\":\"x\"}");

		var export = await _harness.WidgetService.Export(folder.Id,
			[Guid.NewGuid()],
			PortableExportOptions.Default,
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(export.Success, Is.False);
			Assert.That(export.Error, Is.EqualTo(PortabilityError.ValidationError));
		});
	}

	private Task<FolderEntity> AddFolder(Guid profileId, string name, int rows = 3, int columns = 5)
		=> _harness.AddFolder(profileId, name, rows: rows, columns: columns);
}
