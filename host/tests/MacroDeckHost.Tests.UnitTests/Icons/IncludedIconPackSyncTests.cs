using System.Diagnostics;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Icons.Included;
using MacroDeckHost.Application.Icons.Ownership;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Icons;
using MacroDeckHost.Application.Widgets.Icons;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Icons;
using MacroDeckHost.Infrastructure.IconPacks;
using MacroDeckHost.Infrastructure.Icons;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.Icons;

[TestFixture]
internal sealed class IncludedIconPackSyncTests
{
	private static readonly IReadOnlyList<IncludedIconAsset> Embedded = new EmbeddedIncludedIconAssets().Load();

	private IconTestHarness _harness = null!;
	private FakeAssets _assets = null!;
	private CountingProcessor _processor = null!;
	private FakeUsageScanner _usage = null!;
	private StartupReadiness _readiness = null!;
	private ServiceProvider _services = null!;

	[SetUp]
	public void SetUp()
	{
		_harness = new IconTestHarness();
		_harness.Paths.EnsureDirectoriesExist();
		_assets = new FakeAssets(Pick(IncludedIconPack.Cpu, IncludedIconPack.Battery, "zap"));
		_processor = new CountingProcessor(new ImageSharpIconProcessor(_harness.Logger));
		_usage = new FakeUsageScanner();
		_readiness = new StartupReadiness();

		var services = new ServiceCollection();
		services.AddSingleton<IMediator>(_harness.Mediator);
		services.AddScoped<IIconService>(_ => new IconService(_harness.Cache,
			_harness.Storage,
			_harness.FallbackStore,
			_harness.VariantDeriver,
			_harness.Coalescer,
			_harness.Mediator,
			Registry()));
		_services = services.BuildServiceProvider();
	}

	[TearDown]
	public void TearDown()
	{
		_services.Dispose();
		_harness.Dispose();
	}

	[Test]
	public void Every_included_icon_name_is_embedded()
		=> Assert.That(Embedded.Select(asset => asset.Name), Is.EqualTo(IncludedIconPack.Names));

	[Test]
	public async Task The_first_sync_adds_a_read_only_pack_holding_every_included_icon_under_its_fixed_id()
	{
		_assets.Set(Embedded);

		var stopwatch = Stopwatch.StartNew();
		await Sync();
		TestContext.Progress.WriteLine($"First sync of {Embedded.Count} included icons took {stopwatch.ElapsedMilliseconds} ms");

		var pack = _harness.Cache.GetPackById(IncludedIconPack.PackId);
		Assert.That(pack, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(pack!.IsReadOnly, Is.True);
			Assert.That(pack.IsDefault, Is.False);
			Assert.That(pack.SourceId, Is.EqualTo(IncludedIconPack.SourceId));
			Assert.That(pack.SourceRevision, Is.Not.Null);
			Assert.That(_harness.Cache.GetIconCount(IncludedIconPack.PackId), Is.EqualTo(IncludedIconPack.Names.Count));
			foreach (var name in IncludedIconPack.Names)
			{
				var icon = _harness.Cache.GetIconById(IncludedIconPack.IconId(name));
				Assert.That(icon?.Name, Is.EqualTo(name));
				Assert.That(icon?.PackId, Is.EqualTo(IncludedIconPack.PackId));
				Assert.That(icon?.ProcessingState, Is.EqualTo(IconProcessingState.Ready));
			}

			Assert.That(_harness.Mediator.Published.OfType<IconPackCreatedNotification>().Count(), Is.EqualTo(1));
		});
	}

	[Test]
	public async Task An_icon_pack_reference_to_an_included_icon_resolves_to_an_image()
	{
		await Sync();
		var source = new IconPackWidgetIconSource(_harness.Cache, _services.GetRequiredService<IServiceScopeFactory>());
		var reference = IncludedIconPack.IconId(IncludedIconPack.Cpu).ToString();

		var image = await source.GetImageAsync(reference, 128, acceptWebp: true, staticFrame: false, CancellationToken.None);

		Assert.That(image, Is.Not.Null);
		using var bytes = new MemoryStream();
		await image!.Content.CopyToAsync(bytes);
		Assert.Multiple(() =>
		{
			Assert.That(bytes.Length, Is.GreaterThan(0));
			Assert.That(source.GetVersion(reference), Is.Not.Null);
		});
	}

	[Test]
	public async Task A_second_sync_of_the_same_set_keeps_every_id_and_rewrites_nothing()
	{
		await Sync();
		var idsAfterFirst = _harness.Cache.GetIconsByPackId(IncludedIconPack.PackId).Select(icon => icon.Id).Order().ToList();
		var updatedAt = _harness.Cache.GetPackById(IncludedIconPack.PackId)!.UpdatedAt;
		var processed = _processor.Calls;
		var published = _harness.Mediator.Published.Count;

		await Sync();

		Assert.Multiple(() =>
		{
			Assert.That(_harness.Cache.GetIconsByPackId(IncludedIconPack.PackId).Select(icon => icon.Id).Order(),
				Is.EqualTo(idsAfterFirst));
			Assert.That(idsAfterFirst, Is.EquivalentTo(_assets.Load().Select(asset => IncludedIconPack.IconId(asset.Name))));
			Assert.That(_processor.Calls, Is.EqualTo(processed));
			Assert.That(_harness.Mediator.Published.Count, Is.EqualTo(published));
			Assert.That(_harness.Cache.GetPackById(IncludedIconPack.PackId)!.UpdatedAt, Is.EqualTo(updatedAt));
		});
	}

	[Test]
	public async Task The_pack_is_reported_as_built_in_and_cannot_be_deleted()
	{
		await Sync();
		var handler = new GetIconPacksRequestMessageHandler(_harness.Cache, Registry());

		var response = await handler.Handle(new GetIconPacksRequest(), CancellationToken.None);

		var dto = response.Packs.Single(pack => pack.Id == IncludedIconPack.PackId.ToString());
		Assert.Multiple(() =>
		{
			Assert.That(dto.OwnerKind, Is.EqualTo("BuiltIn"));
			Assert.That(dto.CanDelete, Is.False);
			Assert.That(dto.IsReadOnly, Is.True);
			Assert.That(dto.Name, Is.EqualTo(IncludedIconPackSync.PackName));
		});
	}

	[Test]
	public async Task Deleting_renaming_or_importing_into_the_included_pack_is_refused()
	{
		await Sync();
		var packs = new IconPackService(_harness.Cache,
			_harness.BatchTracker,
			_harness.Storage,
			_harness.Mediator,
			Registry(),
			_harness.Logger);
		var imports = _harness.CreateImportService(ownerRegistry: Registry());

		var delete = await packs.Delete(IncludedIconPack.PackId);
		var rename = await packs.Update(IncludedIconPack.PackId, "Mine now", null, null, null);
		var import = await imports.ImportSingle(IncludedIconPack.PackId,
			new IconImportFile("extra.png", new MemoryStream([1, 2, 3])),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(delete.Success, Is.False);
			Assert.That(rename.Success, Is.False);
			Assert.That(import.Error, Is.EqualTo(IconError.PackReadOnly));
			Assert.That(_harness.Cache.GetPackById(IncludedIconPack.PackId)?.Name, Is.EqualTo(IncludedIconPackSync.PackName));
			Assert.That(_harness.Cache.GetIconCount(IncludedIconPack.PackId), Is.EqualTo(3));
		});
	}

	[Test]
	public async Task A_redrawn_icon_replaces_its_files_and_leaves_no_stale_size_behind()
	{
		await Sync();
		var cpu = IncludedIconPack.IconId(IncludedIconPack.Cpu);
		await _harness.Storage.WriteVariant(IncludedIconPack.PackId, cpu, "64-0123456789abcdef", new byte[] { 1 }, CancellationToken.None);
		var redrawn = Pick("gauge")[0].Content;
		_assets.Set([new IncludedIconAsset(IncludedIconPack.Cpu, redrawn), .. Pick(IncludedIconPack.Battery, "zap")]);

		await Sync();

		var icon = _harness.Cache.GetIconById(cpu);
		Assert.Multiple(() =>
		{
			Assert.That(icon?.SourceContentHash, Is.EqualTo(SourceContentHash.Compute(redrawn).Value));
			Assert.That(_harness.Storage.ListVariants(IncludedIconPack.PackId, cpu), Does.Not.Contain("64-0123456789abcdef"));
			Assert.That(_harness.Storage.ListVariants(IncludedIconPack.PackId, cpu), Does.Contain(IconVariants.Master));
		});
	}

	[Test]
	public async Task An_icon_that_fails_leaves_the_revision_unstamped_and_the_next_start_completes_the_pack()
	{
		_processor.FailFor = "zap";
		await Sync();

		Assert.Multiple(() =>
		{
			Assert.That(_harness.Cache.GetPackById(IncludedIconPack.PackId)?.SourceRevision, Is.Null);
			Assert.That(_harness.Cache.GetIconById(IncludedIconPack.IconId(IncludedIconPack.Cpu))?.ProcessingState,
				Is.EqualTo(IconProcessingState.Ready));
			Assert.That(_harness.Cache.GetIconById(IncludedIconPack.IconId("zap")), Is.Null);
		});

		_processor.FailFor = null;
		await Sync();

		Assert.Multiple(() =>
		{
			Assert.That(_harness.Cache.GetPackById(IncludedIconPack.PackId)?.SourceRevision, Is.Not.Null);
			Assert.That(_harness.Cache.GetIconById(IncludedIconPack.IconId("zap"))?.ProcessingState,
				Is.EqualTo(IconProcessingState.Ready));
		});
	}

	[Test]
	public async Task A_pack_without_a_revision_is_synced_again()
	{
		await Sync();
		var pack = _harness.Cache.GetPackById(IncludedIconPack.PackId)!;
		pack.SourceRevision = null;
		await _harness.Cache.AddOrUpdatePack(pack);
		_harness.Storage.DeleteIconFiles(IncludedIconPack.PackId, IncludedIconPack.IconId(IncludedIconPack.Battery));

		await Sync();

		Assert.Multiple(() =>
		{
			Assert.That(_harness.Cache.GetPackById(IncludedIconPack.PackId)?.SourceRevision, Is.Not.Null);
			Assert.That(_harness.Storage.ListVariants(IncludedIconPack.PackId, IncludedIconPack.IconId(IncludedIconPack.Battery)),
				Does.Contain(IconVariants.Master));
		});
	}

	[Test]
	public async Task An_icon_dropped_from_the_set_is_removed_unless_something_still_uses_it()
	{
		await Sync();
		var battery = IncludedIconPack.IconId(IncludedIconPack.Battery);
		var zap = IncludedIconPack.IconId("zap");
		_usage.Referenced.Add(battery);
		_assets.Set(Pick(IncludedIconPack.Cpu));
		_readiness.MarkCachesReady();

		await Sync();

		Assert.Multiple(() =>
		{
			Assert.That(_harness.Cache.GetIconById(zap), Is.Null);
			Assert.That(_harness.Storage.ListVariants(IncludedIconPack.PackId, zap), Is.Empty);
			Assert.That(_harness.Cache.GetIconById(battery), Is.Not.Null);
			Assert.That(_harness.Storage.ListVariants(IncludedIconPack.PackId, battery), Does.Contain(IconVariants.Master));
		});
	}

	[Test]
	public async Task A_dropped_icon_is_kept_while_the_caches_that_reference_icons_are_not_loaded()
	{
		await Sync();
		var previous = Revision();
		_assets.Set(Pick(IncludedIconPack.Cpu));

		await Sync(new IncludedIconPackSyncOptions { CachesReadyBound = TimeSpan.Zero });

		Assert.Multiple(() =>
		{
			Assert.That(_harness.Cache.GetIconById(IncludedIconPack.IconId("zap")), Is.Not.Null);
			Assert.That(Revision(), Is.EqualTo(previous));
		});
	}

	private string? Revision() => _harness.Cache.GetPackById(IncludedIconPack.PackId)?.SourceRevision;

	private Task Sync(IncludedIconPackSyncOptions? options = null) => CreateSync(options).SyncAsync(CancellationToken.None);

	private IncludedIconPackSync CreateSync(IncludedIconPackSyncOptions? options = null)
		=> new(_assets,
			_harness.Cache,
			_harness.Storage,
			_processor,
			_usage,
			_services.GetRequiredService<IServiceScopeFactory>(),
			_readiness,
			TimeProvider.System,
			_harness.Logger,
			options);

	private static IconPackOwnerRegistry Registry()
		=> new([new BuiltInIconPackOwner()]);

	private static List<IncludedIconAsset> Pick(params string[] names)
		=> names.Select(name => Embedded.Single(asset => asset.Name == name)).ToList();

	internal sealed class FakeAssets(IReadOnlyList<IncludedIconAsset> assets) : IIncludedIconAssets
	{
		private IReadOnlyList<IncludedIconAsset> _assets = assets;

		public void Set(IReadOnlyList<IncludedIconAsset> assets) => _assets = assets;

		public IReadOnlyList<IncludedIconAsset> Load() => _assets;
	}

	internal sealed class CountingProcessor(IIconProcessor inner) : IIconProcessor
	{
		private int _calls;

		public int Calls => _calls;

		public string? FailFor { get; set; }

		public Task<Result<ProcessedIconResult, IconError>> Process(Stream original,
			string originalFileName,
			CancellationToken cancellationToken)
		{
			Interlocked.Increment(ref _calls);
			return FailFor is { } name && originalFileName == name + ".svg"
				? Task.FromResult(Result.Fail<ProcessedIconResult, IconError>(IconError.ProcessingFailed, "broken"))
				: inner.Process(original, originalFileName, cancellationToken);
		}
	}

	private sealed class FakeUsageScanner : IIconUsageScanner
	{
		public HashSet<Guid> Referenced { get; } = [];

		public IReadOnlySet<Guid> FindReferencedIconIds() => Referenced;
	}
}
