using System.Text;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Icons.Ownership;
using MacroDeckHost.Application.Ui.Transport.Messages.Icons;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Icons;
using MacroDeckHost.Infrastructure.BackgroundServices;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Infrastructure.Icons;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MacroDeckHost.Tests.UnitTests.Icons;

[TestFixture]
internal sealed class IconAppearanceCatalogTests
{
	private IconTestHarness _harness = null!;
	private IconService _icons = null!;
	private RecordingUiTransport _transport = null!;
	private IconAppearanceEditLocks _editLocks = null!;

	[SetUp]
	public void SetUp()
	{
		_harness = new IconTestHarness();
		_transport = new RecordingUiTransport();
		_editLocks = new IconAppearanceEditLocks();
		_icons = new IconService(_harness.Cache,
			_harness.Storage,
			_harness.FallbackStore,
			_harness.VariantDeriver,
			_harness.Coalescer,
			_harness.Mediator,
			new IconPackOwnerRegistry([]));
	}

	[TearDown]
	public void TearDown() => _harness.Dispose();

	[Test]
	public async Task Appearances_survive_a_restart_nested_under_their_icon_and_never_listed_as_icons()
	{
		var pack = await _harness.CreatePack();
		var plain = await _harness.AddReadyIcon(pack.Id, "plain", Bytes("plain"));
		var icon = await _harness.AddReadyIcon(pack.Id, "lamp", Bytes("lamp"));
		var dark = await AddAppearance(icon, "colorScheme=dark", Bytes("lamp-dark"));

		var json = await File.ReadAllTextAsync(Path.Combine(_harness.Paths.IconPacksDirectory,
			pack.Id.ToString(),
			"pack.json"));
		using var reloaded = new IconPackCache(_harness.PackStore, _harness.Storage, _harness.Logger);
		await reloaded.InitializeCache();
		var manifest = _harness.PackStore.LoadAll().Single();

		Assert.Multiple(() =>
		{
			Assert.That(manifest.Icons.Select(entry => entry.Id), Is.EquivalentTo(new[] { plain.Id, icon.Id }));
			Assert.That(manifest.Icons.Single(entry => entry.Id == icon.Id).Appearances!.Single().Id,
				Is.EqualTo(dark.Id));
			Assert.That(json.Split("\"traits\"").Length - 1, Is.EqualTo(1), "only the appearance carries traits");
			Assert.That(json.Split("\"appearances\"").Length - 1, Is.EqualTo(1), "a plain icon writes no new field");
			Assert.That(reloaded.GetIconsByPackId(pack.Id).Select(i => i.Id), Is.EquivalentTo(new[] { plain.Id, icon.Id }));
			Assert.That(reloaded.GetIconCount(pack.Id), Is.EqualTo(2));
			Assert.That(reloaded.GetAppearances(icon.Id).Single().AppearanceTraits,
				Is.EquivalentTo(new Dictionary<string, string> { ["colorScheme"] = "dark" }));
			Assert.That(reloaded.GetIconById(dark.Id)!.AppearanceOfId, Is.EqualTo(icon.Id));
		});
	}

	[Test]
	public async Task A_pack_written_before_appearances_existed_loads_unchanged()
	{
		var packId = Guid.NewGuid();
		var iconId = Guid.NewGuid();
		var directory = Path.Combine(_harness.Paths.IconPacksDirectory, packId.ToString());
		Directory.CreateDirectory(directory);
		await File.WriteAllTextAsync(Path.Combine(directory, "pack.json"),
			$$"""
			{
			  "id": "{{packId}}", "name": "Old", "isDefault": false, "isReadOnly": false, "sourceType": "User",
			  "createdAt": "2025-01-01T00:00:00Z", "updatedAt": "2025-01-01T00:00:00Z",
			  "icons": [ { "id": "{{iconId}}", "name": "bolt", "isAnimated": false, "state": "Ready",
			    "masterContentHash": "sha256:{{new string('a', 64)}}", "availableSizes": [128],
			    "createdAt": "2025-01-01T00:00:00Z", "updatedAt": "2025-01-01T00:00:00Z" } ]
			}
			""");

		using var cache = new IconPackCache(_harness.PackStore, _harness.Storage, _harness.Logger);
		await cache.InitializeCache();
		var icon = cache.GetIconById(iconId)!;

		Assert.Multiple(() =>
		{
			Assert.That(cache.GetIconsByPackId(packId).Single().Id, Is.EqualTo(iconId));
			Assert.That(icon.AppearanceOfId, Is.Null);
			Assert.That(cache.GetAppearances(iconId), Is.Empty);
			Assert.That(IconMapper.ToDto(icon, cache).ContentHash, Is.EqualTo("sha256:" + new string('a', 64)));
		});
	}

	[Test]
	public async Task Deleting_icons_takes_their_appearances_files_and_entries_with_them()
	{
		var pack = await _harness.CreatePack();
		var single = await _harness.AddReadyIcon(pack.Id, "single", Bytes("single"));
		var singleDark = await AddAppearance(single, "colorScheme=dark", Bytes("single-dark"));
		var bulk = await _harness.AddReadyIcon(pack.Id, "bulk", Bytes("bulk"));
		var bulkStatic = await AddAppearance(bulk, "motion=static", Bytes("bulk-static"));

		await _icons.Delete(single.Id);
		await _icons.DeleteMany([bulk.Id]);
		await _harness.Cache.FlushPendingWrites();
		var manifest = _harness.PackStore.LoadAll().Single();

		Assert.Multiple(() =>
		{
			Assert.That(_harness.Cache.GetIconById(singleDark.Id), Is.Null);
			Assert.That(_harness.Cache.GetIconById(bulkStatic.Id), Is.Null);
			Assert.That(_harness.Storage.ListVariants(pack.Id, singleDark.Id), Is.Empty);
			Assert.That(_harness.Storage.ListVariants(pack.Id, bulkStatic.Id), Is.Empty);
			Assert.That(manifest.Icons, Is.Empty);
			Assert.That(_harness.Mediator.Published.OfType<IconDeletedNotification>().Select(n => n.IconId),
				Is.EquivalentTo(new[] { single.Id, bulk.Id }));
		});
	}

	[TestCase(true)]
	[TestCase(false)]
	public async Task Removing_an_appearance_keeps_the_icon_listed_and_reports_it_updated(bool throughAppearanceEndpoint)
	{
		var pack = await _harness.CreatePack();
		var icon = await _harness.AddReadyIcon(pack.Id, "lamp", Bytes("lamp"));
		var dark = await AddAppearance(icon, "colorScheme=dark", Bytes("lamp-dark"));
		var before = IconMapper.ToDto(icon, _harness.Cache).ContentHash;

		var removed = throughAppearanceEndpoint
			? (await AppearanceService().Remove(icon.Id, dark.Id, CancellationToken.None)).Success
			: (await _icons.Delete(dark.Id)).Success;

		var dto = IconMapper.ToDto(_harness.Cache.GetIconById(icon.Id)!, _harness.Cache);
		Assert.Multiple(() =>
		{
			Assert.That(removed, Is.True);
			Assert.That(_harness.Cache.GetIconsByPackId(pack.Id).Single().Id, Is.EqualTo(icon.Id));
			Assert.That(dto.Appearances, Is.Empty);
			Assert.That(dto.ContentHash, Is.Not.EqualTo(before).And.EqualTo(icon.MasterContentHash));
			Assert.That(_harness.Storage.ListVariants(pack.Id, dark.Id), Is.Empty);
			Assert.That(_harness.Mediator.Published.OfType<IconDeletedNotification>(), Is.Empty);
			Assert.That(_harness.Mediator.Published.OfType<IconUpdatedNotification>().Select(n => n.Icon.Id),
				Is.EqualTo(new[] { icon.Id }));
		});
	}

	[Test]
	public async Task A_processed_appearance_reports_only_its_icon_and_changes_the_icons_version()
	{
		var pack = await _harness.CreatePack();
		var icon = await _harness.AddReadyIcon(pack.Id, "lamp", Bytes("lamp"));
		var before = IconMapper.ToDto(icon, _harness.Cache);

		var added = await AppearanceService().AddOrReplace(icon.Id,
			"colorScheme=dark",
			new IconImportFile("lamp-dark.png", new MemoryStream(Png(new Rgba32(10, 10, 10)))),
			CancellationToken.None);
		await using (var processing = await StartProcessing())
		{
			await WaitUntil(() => _harness.Cache.GetAppearances(icon.Id) is [{ ProcessingState: IconProcessingState.Ready }]);
		}

		var after = IconMapper.ToDto(_harness.Cache.GetIconById(icon.Id)!, _harness.Cache);
		var announced = _harness.Mediator.Published.OfType<IconUpdatedNotification>().Select(n => n.Icon.Id).ToList();
		Assert.Multiple(() =>
		{
			Assert.That(added.Success, Is.True);
			Assert.That(before.Appearances, Is.Empty);
			Assert.That(after.Appearances.Single().Key, Is.EqualTo("colorScheme=dark"));
			Assert.That(after.Appearances.Single().ProcessingState, Is.EqualTo(nameof(IconProcessingState.Ready)));
			Assert.That(after.ContentHash, Is.Not.EqualTo(before.ContentHash));
			Assert.That(announced, Is.Not.Empty.And.All.EqualTo(icon.Id));
			Assert.That(_harness.Mediator.Published.OfType<IconsAddedNotification>(), Is.Empty);
			Assert.That(_harness.Cache.GetIconsByPackId(pack.Id).Single().Id, Is.EqualTo(icon.Id));
		});
	}

	[Test]
	public async Task An_appearance_cannot_be_added_to_a_read_only_pack_or_to_an_appearance()
	{
		var readOnly = await _harness.CreatePack("Store pack", isReadOnly: true);
		var locked = await _harness.AddReadyIcon(readOnly.Id, "locked", Bytes("locked"));
		var pack = await _harness.CreatePack();
		var icon = await _harness.AddReadyIcon(pack.Id, "lamp", Bytes("lamp"));
		var dark = await AddAppearance(icon, "colorScheme=dark", Bytes("lamp-dark"));
		var service = AppearanceService();

		var onReadOnly = await service.AddOrReplace(locked.Id, "colorScheme=dark", Upload("a.png"), CancellationToken.None);
		var onAppearance = await service.AddOrReplace(dark.Id, "motion=static", Upload("b.png"), CancellationToken.None);
		var badKey = await service.AddOrReplace(icon.Id, "Dark", Upload("c.png"), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(onReadOnly.Error, Is.EqualTo(IconError.PackReadOnly));
			Assert.That(onAppearance.Error, Is.EqualTo(IconError.NotFound));
			Assert.That(badKey.Error, Is.EqualTo(IconError.ValidationError));
			Assert.That(_harness.Cache.GetAppearances(locked.Id), Is.Empty);
		});
	}

	[Test]
	public async Task Concurrent_uploads_of_one_kind_leave_the_icon_with_a_single_appearance_of_that_kind()
	{
		var pack = await _harness.CreatePack();
		var icon = await _harness.AddReadyIcon(pack.Id, "lamp", Bytes("lamp"));

		var results = await Task.WhenAll(Enumerable.Range(0, 8)
			.Select(index => Task.Run(() => AppearanceService().AddOrReplace(icon.Id,
				"colorScheme=dark",
				Upload($"lamp-dark-{index}.png"),
				CancellationToken.None))));

		Assert.Multiple(() =>
		{
			Assert.That(results.Select(result => result.Success), Is.All.True);
			Assert.That(_harness.Cache.GetAppearances(icon.Id).Select(asset => IconAppearanceTraits.ToKey(asset.AppearanceTraits!)),
				Is.EqualTo(new[] { "colorScheme=dark" }));
		});
	}

	[Test]
	public async Task Renaming_an_icon_renames_its_appearances_in_the_catalog_and_the_stored_pack()
	{
		var pack = await _harness.CreatePack();
		var icon = await _harness.AddReadyIcon(pack.Id, "lamp", Bytes("lamp"));
		var dark = await AddAppearance(icon, "colorScheme=dark", Bytes("lamp-dark"));

		var renamed = await _icons.Rename(icon.Id, "bulb");
		await _harness.Cache.FlushPendingWrites();

		var stored = _harness.PackStore.LoadAll().Single().Icons.Single();
		Assert.Multiple(() =>
		{
			Assert.That(renamed.Success, Is.True);
			Assert.That(_harness.Cache.GetIconById(dark.Id)!.Name, Is.EqualTo("bulb"));
			Assert.That(stored.Appearances!.Single().Name, Is.EqualTo("bulb"));
		});
	}

	private IconAppearanceService AppearanceService(IIconReferenceRewriter? rewriter = null)
		=> new(_harness.Cache,
			_harness.Storage,
			_harness.BatchTracker,
			_harness.BatchFinalizer,
			_harness.ProcessingChannel,
			_harness.Coalescer,
			new IconPackOwnerRegistry([]),
			rewriter ?? new NoReferences(),
			_transport,
			_harness.Mediator,
			_editLocks);

	private async Task<IAsyncDisposable> StartProcessing()
	{
		var provider = new ServiceCollection().AddSingleton<IMediator>(_harness.Mediator).BuildServiceProvider();
		var service = new IconProcessingBackgroundService(new StartedHostLifetime(),
			_harness.Cache,
			_harness.Storage,
			new ImageSharpIconProcessor(_harness.Logger),
			_harness.BatchTracker,
			_harness.BatchFinalizer,
			_harness.ProcessingChannel,
			_harness.CancellationRegistry,
			_harness.Coalescer,
			provider.GetRequiredService<IServiceScopeFactory>(),
			_harness.Logger);
		await service.StartAsync(CancellationToken.None);
		return new Running(service, provider);
	}

	private async Task<IconEntity> AddAppearance(IconEntity parent, string key, byte[] master)
	{
		Assert.That(IconAppearanceTraits.TryParse(key, out var traits), Is.True);
		var asset = new IconEntity
		{
			Id = Guid.CreateVersion7(),
			PackId = parent.PackId,
			Name = parent.Name,
			SourceContentHash = SourceContentHash.Compute(master).Value,
			MasterContentHash = MasterContentHash.Compute(master).Value,
			ProcessingState = IconProcessingState.Ready,
			AppearanceOfId = parent.Id,
			AppearanceTraits = traits,
			CreatedAt = DateTime.UtcNow
		};
		await _harness.Cache.AddIcons(parent.PackId, [asset]);
		await _harness.Storage.WriteVariant(parent.PackId, asset.Id, IconVariants.Master, master, CancellationToken.None);
		return asset;
	}

	private static IconImportFile Upload(string name) => new(name, new MemoryStream(Png(new Rgba32(1, 2, 3))));

	private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

	private static byte[] Png(Rgba32 color)
	{
		using var image = new Image<Rgba32>(64, 64, color);
		using var stream = new MemoryStream();
		image.SaveAsPng(stream);
		return stream.ToArray();
	}

	private static async Task WaitUntil(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow.AddSeconds(15);
		while (!condition())
		{
			if (DateTime.UtcNow > deadline)
			{
				Assert.Fail("Timed out waiting for the appearance to process");
			}

			await Task.Delay(50);
		}
	}

	private sealed class NoReferences : IIconReferenceRewriter
	{
		public Task<int> Replace(Guid fromIconId, Guid toIconId, CancellationToken cancellationToken)
			=> Task.FromResult(0);
	}

	private sealed class Running(IconProcessingBackgroundService service, ServiceProvider provider) : IAsyncDisposable
	{
		public async ValueTask DisposeAsync()
		{
			await service.StopAsync(CancellationToken.None);
			service.Dispose();
			await provider.DisposeAsync();
		}
	}

	private sealed class StartedHostLifetime : IHostApplicationLifetime
	{
		public CancellationToken ApplicationStarted { get; } = new(canceled: true);
		public CancellationToken ApplicationStopping { get; } = CancellationToken.None;
		public CancellationToken ApplicationStopped { get; } = CancellationToken.None;

		public void StopApplication()
		{
		}
	}
}
