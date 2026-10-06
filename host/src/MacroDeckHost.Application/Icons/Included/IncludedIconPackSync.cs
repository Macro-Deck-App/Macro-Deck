using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Icons;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Application.Icons.Included;

public sealed class IncludedIconPackSyncOptions
{
	public TimeSpan CachesReadyBound { get; init; } = TimeSpan.FromSeconds(30);

	public int MaxParallelism { get; init; } = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
}

public sealed class IncludedIconPackSync : IIncludedIconPackSync
{
	public const string PackName = "Included";
	public const string PackAuthor = "Lucide (ISC license)";

	private readonly IIncludedIconAssets _assets;
	private readonly IIconPackCache _iconPackCache;
	private readonly IIconStorage _storage;
	private readonly IIconProcessor _processor;
	private readonly IIconUsageScanner _usageScanner;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly StartupReadiness _readiness;
	private readonly TimeProvider _timeProvider;
	private readonly IncludedIconPackSyncOptions _options;
	private readonly ILogger _logger;

	public IncludedIconPackSync(IIncludedIconAssets assets,
		IIconPackCache iconPackCache,
		IIconStorage storage,
		IIconProcessor processor,
		IIconUsageScanner usageScanner,
		IServiceScopeFactory scopeFactory,
		StartupReadiness readiness,
		TimeProvider timeProvider,
		ILogger logger,
		IncludedIconPackSyncOptions? options = null)
	{
		_assets = assets;
		_iconPackCache = iconPackCache;
		_storage = storage;
		_processor = processor;
		_usageScanner = usageScanner;
		_scopeFactory = scopeFactory;
		_readiness = readiness;
		_timeProvider = timeProvider;
		_options = options ?? new IncludedIconPackSyncOptions();
		_logger = logger.ForContext<IncludedIconPackSync>();
	}

	public async Task SyncAsync(CancellationToken cancellationToken)
	{
		var stopwatch = Stopwatch.StartNew();
		var assets = _assets.Load();
		var revision = Revision(assets);
		var pack = _iconPackCache.GetPackById(IncludedIconPack.PackId);
		if (pack is not null && string.Equals(pack.SourceRevision, revision, StringComparison.Ordinal) &&
			assets.All(IsCurrent))
		{
			return;
		}

		var created = pack is null;
		var now = _timeProvider.GetUtcNow().UtcDateTime;
		pack ??= new IconPackEntity { Id = IncludedIconPack.PackId, Name = PackName, CreatedAt = now };
		pack.Name = PackName;
		pack.Author = PackAuthor;
		pack.IsDefault = false;
		pack.IsReadOnly = true;
		pack.SourceType = IconPackSourceType.User;
		pack.SourceId = IncludedIconPack.SourceId;
		pack.UpdatedAt = now;
		await _iconPackCache.AddOrUpdatePack(pack);

		var written = 0;
		var failed = 0;
		var parallel = new ParallelOptions
		{
			MaxDegreeOfParallelism = _options.MaxParallelism,
			CancellationToken = cancellationToken
		};
		await Parallel.ForEachAsync(assets.Where(asset => !IsCurrent(asset)),
			parallel,
			async (asset, token) =>
			{
				if (await TryWriteIcon(asset, now, token))
				{
					Interlocked.Increment(ref written);
				}
				else
				{
					Interlocked.Increment(ref failed);
				}
			});

		var droppedHandled = await RemoveDroppedIcons(assets, cancellationToken);

		// Stamped last so a sync that was interrupted or left an icon behind is retried on the next start.
		if (failed == 0 && droppedHandled)
		{
			pack.SourceRevision = revision;
			await _iconPackCache.AddOrUpdatePack(pack);
		}

		await Announce(pack, created, cancellationToken);

		_logger.Information(
			"Synced the included icon pack in {ElapsedMs} ms: {Written} icons written, {Failed} failed",
			stopwatch.ElapsedMilliseconds,
			written,
			failed);
	}

	private bool IsCurrent(IncludedIconAsset asset)
	{
		var id = IncludedIconPack.IconId(asset.Name);
		return _iconPackCache.GetIconById(id) is { ProcessingState: IconProcessingState.Ready } icon &&
			icon.PackId == IncludedIconPack.PackId &&
			string.Equals(icon.SourceContentHash, SourceContentHash.Compute(asset.Content).Value, StringComparison.Ordinal) &&
			_storage.ListVariants(IncludedIconPack.PackId, id).Contains(IconVariants.Master);
	}

	private async Task<bool> TryWriteIcon(IncludedIconAsset asset, DateTime now, CancellationToken cancellationToken)
	{
		var id = IncludedIconPack.IconId(asset.Name);
		var fileName = asset.Name + ".svg";
		try
		{
			ProcessedIconResult processed;
			await using (var content = new MemoryStream(asset.Content, writable: false))
			{
				var result = await _processor.Process(content, fileName, cancellationToken);
				if (!result.Success)
				{
					_logger.Warning("Included icon {Name} could not be processed: {Error}",
						asset.Name,
						result.ErrorMessage ?? result.Error.ToString());
					return false;
				}

				processed = result.Data!;
			}

			// Sizes derived from the previous master are otherwise only swept by a later derivation.
			_storage.DeleteIconFiles(IncludedIconPack.PackId, id);
			foreach (var (size, bytes) in processed.Variants.OrderBy(variant => variant.Key))
			{
				await _storage.WriteVariant(IncludedIconPack.PackId,
					id,
					size.ToString(CultureInfo.InvariantCulture),
					bytes,
					cancellationToken);
			}

			await _storage.WriteVariant(IncludedIconPack.PackId,
				id,
				IconVariants.Master,
				processed.MasterWebp,
				cancellationToken);

			var existing = _iconPackCache.GetIconById(id);
			var icon = existing ?? new IconEntity
			{
				Id = id,
				PackId = IncludedIconPack.PackId,
				Name = asset.Name,
				CreatedAt = now
			};
			icon.Name = asset.Name;
			icon.Width = processed.Width;
			icon.Height = processed.Height;
			icon.IsAnimated = processed.IsAnimated;
			icon.FrameCount = processed.FrameCount;
			icon.SourceContentHash = processed.SourceContentHash.Value;
			icon.MasterContentHash = processed.MasterContentHash.Value;
			icon.OriginalFileName = fileName;
			icon.OriginalFormat = processed.OriginalFormat;
			icon.AvailableSizes = processed.Variants.Keys.OrderBy(size => size).ToList();
			icon.ProcessingState = IconProcessingState.Ready;
			icon.ProcessingError = null;
			icon.UpdatedAt = now;

			if (existing is null)
			{
				await _iconPackCache.AddIcons(IncludedIconPack.PackId, [icon]);
			}
			else
			{
				await _iconPackCache.UpdateIcon(icon);
			}

			return true;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.Warning(ex, "Included icon {Name} could not be written", asset.Name);
			return false;
		}
	}

	private async Task<bool> RemoveDroppedIcons(IReadOnlyList<IncludedIconAsset> assets,
		CancellationToken cancellationToken)
	{
		var expected = assets.Select(asset => IncludedIconPack.IconId(asset.Name)).ToHashSet();
		var dropped = _iconPackCache.GetIconsByPackId(IncludedIconPack.PackId)
			.Where(icon => !expected.Contains(icon.Id))
			.ToList();
		if (dropped.Count == 0)
		{
			return true;
		}

		// The usage scan reads the folder, automation and script caches; before they have loaded it finds no
		// references at all and would remove icons that are still in use.
		try
		{
			await _readiness.WhenCachesReady.WaitAsync(_options.CachesReadyBound, _timeProvider, cancellationToken);
		}
		catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
		{
			_logger.Warning(ex, "Icons dropped from the included icon pack are kept until the caches are ready");
			return false;
		}

		var inUse = _usageScanner.FindReferencedIconIds();
		var removable = dropped.Where(icon => !inUse.Contains(icon.Id)).Select(icon => icon.Id).ToList();
		await _iconPackCache.RemoveIcons(IncludedIconPack.PackId, removable);
		foreach (var id in removable)
		{
			_storage.DeleteIconFiles(IncludedIconPack.PackId, id);
		}

		return true;
	}

	private async Task Announce(IconPackEntity pack, bool created, CancellationToken cancellationToken)
	{
		await using var scope = _scopeFactory.CreateAsyncScope();
		var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
		var iconCount = _iconPackCache.GetIconCount(pack.Id);
		if (created)
		{
			await mediator.Publish(new IconPackCreatedNotification(pack, iconCount), cancellationToken);
		}
		else
		{
			await mediator.Publish(new IconPackUpdatedNotification(pack, iconCount), cancellationToken);
		}
	}

	private static string Revision(IReadOnlyList<IncludedIconAsset> assets)
	{
		using var hash = ContentHash.CreateIncremental();
		foreach (var asset in assets.OrderBy(asset => asset.Name, StringComparer.Ordinal))
		{
			hash.Append(Encoding.UTF8.GetBytes(asset.Name));
			hash.Append([0]);
			hash.Append(SHA256.HashData(asset.Content));
		}

		return hash.Finish();
	}
}
