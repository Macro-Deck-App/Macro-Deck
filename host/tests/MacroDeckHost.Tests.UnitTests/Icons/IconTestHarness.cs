using MacroDeckHost.Application.Icons;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Icons;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Infrastructure.Icons;
using MacroDeckHost.Infrastructure.Persistence;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Tests.UnitTests.Icons;

internal sealed class IconTestHarness : IDisposable
{
	public TestPaths Paths { get; } = new();
	public ILogger Logger { get; } = new LoggerConfiguration().CreateLogger();
	public RecordingMediator Mediator { get; } = new();

	public JsonIconPackStore PackStore { get; }
	public JsonIconImportBatchStore BatchStore { get; }
	public IconPackCache Cache { get; }
	public FileSystemIconStorage Storage { get; }
	public IconImportBatchTracker BatchTracker { get; }
	public IconImportBatchFinalizer BatchFinalizer { get; }
	public IconImportCancellationRegistry CancellationRegistry { get; } = new();
	public IconProcessingChannel ProcessingChannel { get; } = new();
	public IconImportCoalescer Coalescer { get; } = new();

	public IconTestHarness()
	{
		PackStore = new JsonIconPackStore(Paths, Logger);
		BatchStore = new JsonIconImportBatchStore(Paths, Logger);
		Storage = new FileSystemIconStorage(Paths, Logger);
		Cache = new IconPackCache(PackStore, Storage, Logger);
		FallbackStore = new ImageSharpIconFallbackStore(Storage, Paths, Logger);
		BatchTracker = new IconImportBatchTracker(BatchStore);
		BatchFinalizer = new IconImportBatchFinalizer(Cache, BatchTracker, Storage, Logger);
		RestoreService = new IconPackRestoreService(Cache, Storage, PackStore, Paths, Mediator, Logger);
	}

	public ImageSharpIconFallbackStore FallbackStore { get; }

	public IconPackRestoreService RestoreService { get; }

	public IconImportService CreateImportService(IAppIconExtractor? appIconExtractor = null)
		=> new(Cache,
			Storage,
			BatchTracker,
			BatchFinalizer,
			ProcessingChannel,
			RestoreService,
			CancellationRegistry,
			Coalescer,
			appIconExtractor ?? new FakeAppIconExtractor(),
			Mediator,
			Logger);

	public IconPackExportService CreateExportService() => new(Cache, Storage, Logger);

	public async Task<IconPackEntity> CreatePack(string name = "Test Pack",
		bool isDefault = false,
		bool isReadOnly = false)
	{
		var pack = new IconPackEntity
		{
			Id = Guid.CreateVersion7(),
			Name = name,
			IsDefault = isDefault,
			IsReadOnly = isReadOnly,
			CreatedAt = DateTime.UtcNow
		};
		await Cache.AddOrUpdatePack(pack);
		return pack;
	}

	public async Task<IconEntity> AddReadyIcon(Guid packId, string name, byte[] master, byte[]? source = null)
	{
		source ??= master;
		var icon = new IconEntity
		{
			Id = Guid.CreateVersion7(),
			PackId = packId,
			Name = name,
			SourceContentHash = SourceContentHash.Compute(source).Value,
			MasterContentHash = MasterContentHash.Compute(master).Value,
			ProcessingState = IconProcessingState.Ready,
			CreatedAt = DateTime.UtcNow
		};
		await Cache.AddIcons(packId, [icon]);
		await Storage.WriteVariant(packId, icon.Id, IconVariants.Master, master, CancellationToken.None);
		return icon;
	}

	public void Dispose()
	{
		Cache.Dispose();
		FallbackStore.Dispose();
		Paths.Cleanup();
	}
}
