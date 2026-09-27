using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeck.Plugin.Protocol.Assets;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Icons.Ownership;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Actions;
using MacroDeckHost.Application.Plugins.IconPacks;
using MacroDeckHost.Application.Plugins.Runtime;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Widgets.Icons;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Icons;
using MacroDeckHost.Infrastructure.Icons;
using MacroDeckHost.Infrastructure.Notifications;
using MacroDeckHost.Infrastructure.Plugins.IconPacks;
using MacroDeckHost.Tests.UnitTests.Icons;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MacroDeckHost.Tests.UnitTests.Plugins.IconPacks;

internal sealed class PluginIconPackTestHost : IDisposable
{
	private readonly ServiceProvider _services;

	public PluginIconPackTestHost(IPluginInstallationCatalog? catalog = null,
		bool ready = true,
		TimeSpan? readinessBound = null)
	{
		Catalog = catalog ?? new FakePluginInstallationCatalog();
		Sessions = new PluginSessionRegistry(TimeProvider.System, Serilog.Core.Logger.None);
		Declarations = new PluginBundledIconPackDeclarations(Catalog, new PluginManifestReader(), Serilog.Core.Logger.None);
		Owner = new PluginIconPackOwner(Declarations);
		OwnerRegistry = new IconPackOwnerRegistry([Owner]);
		Restore = Icons.CreateRestoreService(OwnerRegistry);
		Resolver = new PluginIconResolver(Icons.Cache, Declarations);

		var services = new ServiceCollection();
		services.AddSingleton<IIconPackRestoreService>(Restore);
		services.AddSingleton<IMediator>(Icons.Mediator);
		services.AddSingleton(TestLocalization.Preferences);
		services.AddSingleton<IIconService>(IconService());
		_services = services.BuildServiceProvider();
		UiResources = new PluginIconUiResources(Icons.Cache,
			Resolver,
			new WidgetIconSourceRegistry([new IconPackWidgetIconSource(Icons.Cache, _services.GetRequiredService<IServiceScopeFactory>())]));

		if (ready)
		{
			Readiness.MarkCachesReady();
			Readiness.MarkIconPacksReady();
		}

		Sync = new PluginIconPackSync(Declarations,
			Icons.Cache,
			Usage,
			_services.GetRequiredService<IServiceScopeFactory>(),
			Notifications,
			TestLocalization.Resolver,
			Invalidator,
			new RemoteIconProviderActionRegistry(Snapshots, null!, new InMemoryPluginAssetCache(), Resolver),
			Sessions,
			Catalog,
			Readiness,
			TimeProvider.System,
			Icons.Logger,
			new PluginIconPackSyncOptions { ReadinessBound = readinessBound ?? TimeSpan.FromSeconds(30) });
	}

	public IconTestHarness Icons { get; } = new();

	public IPluginInstallationCatalog Catalog { get; }

	public PluginSessionRegistry Sessions { get; }

	public PluginBundledIconPackDeclarations Declarations { get; }

	public PluginIconPackOwner Owner { get; }

	public IconPackOwnerRegistry OwnerRegistry { get; }

	public IconPackRestoreService Restore { get; }

	public PluginIconResolver Resolver { get; }

	public PluginIconUiResources UiResources { get; }

	public IServiceScopeFactory ScopeFactory => _services.GetRequiredService<IServiceScopeFactory>();

	public FixedIconUsage Usage { get; } = new();

	public UserNotificationStore Notifications { get; } = new();

	public FakeWidgetIconInvalidator Invalidator { get; } = new();

	public IconProviderSnapshots Snapshots { get; } = new();

	public StartupReadiness Readiness { get; } = new();

	public PluginIconPackSync Sync { get; }

	public Task<PluginIconPackSyncResult> SyncDevelopment(string pluginId, params (string Key, byte[] Archive)[] packs)
		=> Sync.SyncDevelopmentAsync(pluginId,
			"session-1",
			"Test Plugin",
			packs.Select(pack => new DevelopmentIconPack(pack.Key, AssetContentHash.Compute(pack.Archive), pack.Archive)).ToList(),
			CancellationToken.None);

	public IconPackEntity? PluginPack(string pluginId, string key)
		=> Icons.Cache.GetAllPacks()
			.SingleOrDefault(pack => pack.SourceType == IconPackSourceType.Plugin &&
				pack.SourceId == PluginIconReferences.SourceId(pluginId, key));

	public IconEntity Icon(IconPackEntity pack, string name)
		=> Icons.Cache.GetIconsByPackId(pack.Id).Single(icon => icon.Name == name);

	public IconPackService PackService() => new(Icons.Cache,
		Icons.BatchTracker,
		Icons.Storage,
		Icons.Mediator,
		OwnerRegistry,
		Icons.Logger);

	public IconService IconService() => new(Icons.Cache,
		Icons.Storage,
		Icons.FallbackStore,
		Icons.VariantDeriver,
		Icons.Coalescer,
		Icons.Mediator,
		OwnerRegistry);

	public async Task<byte[]> BuildArchive(string packName, params (string Name, string Body)[] icons)
	{
		var source = await Icons.CreatePack($"{packName}-{Guid.NewGuid():N}");
		source.Name = packName;
		await Icons.Cache.AddOrUpdatePack(source);

		foreach (var (name, body) in icons)
		{
			var master = await WebpFor(body);
			var icon = new IconEntity
			{
				Id = Guid.CreateVersion7(),
				PackId = source.Id,
				Name = name,
				MasterContentHash = MasterContentHash.Compute(master).Value,
				ProcessingState = IconProcessingState.Ready,
				CreatedAt = DateTime.UtcNow
			};
			await Icons.Cache.AddIcons(source.Id, [icon]);
			await Icons.Storage.WriteVariant(source.Id, icon.Id, IconVariants.Master, master, CancellationToken.None);
		}

		using var archive = new MemoryStream();
		var exported = await Icons.CreateExportService().Export(source.Id, archive, CancellationToken.None);
		Assert.That(exported.Success, Is.True);
		await Icons.Cache.RemovePack(source.Id);
		return archive.ToArray();
	}

	public void Dispose()
	{
		Sync.Dispose();
		_services.Dispose();
		Icons.Dispose();
	}

	private static async Task<byte[]> WebpFor(string body)
	{
		var seed = (byte)(body.Aggregate(17, (hash, character) => (hash * 31) + character) & 0xFF);
		using var image = new Image<Rgba32>(4, 4, new Rgba32(seed, (byte)(255 - seed), 64, 255));
		using var buffer = new MemoryStream();
		await image.SaveAsWebpAsync(buffer);
		return buffer.ToArray();
	}
}

internal sealed class FixedIconUsage : IIconUsageScanner
{
	public HashSet<Guid> InUse { get; } = [];

	public IReadOnlySet<Guid> FindReferencedIconIds() => InUse;
}

internal sealed class IconProviderSnapshots : IRemotePluginSnapshotStore
{
	private readonly Dictionary<string, RemotePluginCapabilitySnapshot> _byPluginId = new(StringComparer.Ordinal);

	public void DeclareIconProvider(string pluginId, string actionId)
		=> _byPluginId[pluginId] = RemotePluginCapabilitySnapshot.Empty(pluginId) with
		{
			Actions = [new RemoteActionDescriptor(actionId, "Action", string.Empty, [], null, false, false, false, true)]
		};

	public RemotePluginCapabilitySnapshot GetSnapshot(string pluginId)
		=> _byPluginId.TryGetValue(pluginId, out var snapshot) ? snapshot : RemotePluginCapabilitySnapshot.Empty(pluginId);

	public bool Has(string pluginId) => _byPluginId.ContainsKey(pluginId);

	public Task SaveAsync(RemotePluginCapabilitySnapshot snapshot, CancellationToken cancellationToken = default)
	{
		_byPluginId[snapshot.PluginId] = snapshot;
		return Task.CompletedTask;
	}
}
