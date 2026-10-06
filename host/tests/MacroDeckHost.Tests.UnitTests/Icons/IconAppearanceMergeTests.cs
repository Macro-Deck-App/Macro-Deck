using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Icons.Ownership;
using MacroDeckHost.Application.Ui.Transport.Messages.Icons;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Tests.UnitTests.Portable;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Icons;

[TestFixture]
internal sealed class IconAppearanceMergeTests
{
	private PortabilityTestHarness _harness = null!;
	private StubAutomationCache _automations = null!;
	private RecordingUiTransport _transport = null!;
	private IconAppearanceService _service = null!;

	[SetUp]
	public void SetUp()
	{
		_harness = new PortabilityTestHarness();
		_automations = new StubAutomationCache();
		_transport = new RecordingUiTransport();
		var icons = _harness.Icons;
		_service = new IconAppearanceService(icons.Cache,
			icons.Storage,
			icons.BatchTracker,
			icons.BatchFinalizer,
			icons.ProcessingChannel,
			icons.Coalescer,
			new IconPackOwnerRegistry([]),
			new IconReferenceRewriter(_harness.FolderCache, _automations, _harness.ScriptCache, icons.Mediator),
			_transport,
			icons.Mediator,
			new IconAppearanceEditLocks());
	}

	[TearDown]
	public void TearDown() => _harness.Dispose();

	[Test]
	public async Task Merging_an_icon_moves_every_use_to_the_target_and_takes_it_out_of_the_pack_listing()
	{
		var pack = await _harness.Icons.CreatePack();
		var target = await _harness.AddReadyIcon(pack.Id, "lamp", [128]);
		var source = await _harness.AddReadyIcon(pack.Id, "lamp-dark", [128]);
		var (_, folder, widget) = await _harness.SeedProfile(
			$$$"""{"label":"Lamp","icon":{"type":"icon-pack","reference":"{{{source.Id}}}"}}""");
		var automation = _automations.Add($$$"""[{"action":"set-icon","parameters":{"icon":"{{{source.Id}}}"}}]""");
		var countBefore = _harness.Icons.Cache.GetIconCount(pack.Id);

		var result = await _service.Merge(target.Id, source.Id, "colorScheme=dark", CancellationToken.None);

		var stillServed = await ServedMaster(source.Id);
		var rewritten = _harness.FolderCache.GetFolderById(folder.Id)!.Widgets.Single(w => w.Id == widget.Id).Data!;
		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(result.Data!.MergedIconId, Is.EqualTo(source.Id));
			Assert.That(rewritten, Does.Contain(target.Id.ToString()).And.Not.Contain(source.Id.ToString()));
			Assert.That(_automations.GetById(automation.Id)!.Flows, Does.Contain(target.Id.ToString()));
			Assert.That(_harness.Icons.Cache.GetIconsByPackId(pack.Id).Select(i => i.Id), Is.EqualTo(new[] { target.Id }));
			Assert.That(_harness.Icons.Cache.GetIconCount(pack.Id), Is.EqualTo(countBefore - 1));
			Assert.That(_harness.Icons.Cache.GetAppearances(target.Id).Single().Id, Is.EqualTo(source.Id));
			Assert.That(_transport.Broadcasts.OfType<IconDeletedEvent>().Single().IconId, Is.EqualTo(source.Id.ToString()));
			Assert.That(stillServed, Is.EqualTo(PortabilityTestHarness.MasterBytesFor("lamp-dark")),
				"a handle a plugin or device already holds for the merged icon keeps drawing it");
			Assert.That(_harness.Icons.Mediator.Published.OfType<IconUpdatedNotification>().Single().Icon.Id,
				Is.EqualTo(target.Id));
			Assert.That(_harness.Icons.Mediator.Published.OfType<IconPackUpdatedNotification>().Single().IconCount,
				Is.EqualTo(countBefore - 1));
			Assert.That(_harness.Icons.Mediator.Published.OfType<WidgetUpdatedNotification>().Single().Widget.Id,
				Is.EqualTo(widget.Id));
		});
	}

	[Test]
	public async Task An_icon_with_appearances_of_its_own_or_from_another_pack_is_not_merged()
	{
		var pack = await _harness.Icons.CreatePack();
		var other = await _harness.Icons.CreatePack("Other");
		var target = await _harness.AddReadyIcon(pack.Id, "lamp", [128]);
		var foreign = await _harness.AddReadyIcon(other.Id, "foreign", [128]);
		var withAppearance = await _harness.AddReadyIcon(pack.Id, "busy", [128]);
		var owned = await _harness.AddReadyIcon(pack.Id, "owned", [128]);
		Assert.That((await _service.Merge(withAppearance.Id, owned.Id, "motion=static", CancellationToken.None)).Success,
			Is.True);

		var fromOtherPack = await _service.Merge(target.Id, foreign.Id, "colorScheme=dark", CancellationToken.None);
		var nested = await _service.Merge(target.Id, withAppearance.Id, "colorScheme=dark", CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(fromOtherPack.Error, Is.EqualTo(IconError.ValidationError));
			Assert.That(nested.Error, Is.EqualTo(IconError.ValidationError));
			Assert.That(_harness.Icons.Cache.GetAppearances(target.Id), Is.Empty);
		});
	}

	private async Task<byte[]?> ServedMaster(Guid iconId)
	{
		var icons = _harness.Icons;
		var service = new IconService(icons.Cache,
			icons.Storage,
			icons.FallbackStore,
			icons.VariantDeriver,
			icons.Coalescer,
			icons.Mediator,
			new IconPackOwnerRegistry([]));
		var image = await service.GetImage(iconId, null, true, false, CancellationToken.None);
		if (image.Data is not { } served)
		{
			return null;
		}

		await using var content = served.Content;
		using var buffer = new MemoryStream();
		await content.CopyToAsync(buffer);
		return buffer.ToArray();
	}
}
