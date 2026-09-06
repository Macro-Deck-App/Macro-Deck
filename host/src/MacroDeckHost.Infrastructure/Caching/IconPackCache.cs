using System.Collections.Concurrent;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Icons;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Caching;

public sealed class IconPackCache : IIconPackCache, IDisposable
{
	private static readonly TimeSpan _flushDelay = TimeSpan.FromMilliseconds(500);

	private readonly IIconPackStore _store;
	private readonly IIconStorage _iconStorage;
	private readonly ILogger _logger;
	private readonly ConcurrentDictionary<Guid, IconPackEntity> _packs = new();
	private readonly ConcurrentDictionary<Guid, IconEntity> _icons = new();
	private readonly ConcurrentDictionary<Guid, byte> _dirtyPacks = new();
	private readonly ContentIndex _sourceIndex = new();
	private readonly ContentIndex _masterIndex = new();

	// What each icon currently contributes to the two indexes. Required because icons are mutated by
	// reference - the background worker sets the hashes on the very object already held here and only then
	// calls UpdateIcon, so the previous values are gone by the time the index would need to drop them.
	private readonly ConcurrentDictionary<Guid, IndexedHashes> _indexedHashes = new();
	private readonly SemaphoreSlim _initializationLock = new(1, 1);
	private readonly SemaphoreSlim _updateLock = new(1, 1);
	private readonly Timer _flushTimer;
	private bool _isInitialized;
	private bool _disposed;

	public IconPackCache(IIconPackStore store, IIconStorage iconStorage, ILogger logger)
	{
		_store = store;
		_iconStorage = iconStorage;
		_logger = logger;
		_flushTimer = new Timer(OnFlushTimer, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
	}

	public async Task InitializeCache()
	{
		if (_isInitialized)
		{
			return;
		}

		await _initializationLock.WaitAsync();
		try
		{
			if (_isInitialized)
			{
				return;
			}

			foreach (var manifest in _store.LoadAll())
			{
				_packs.TryAdd(manifest.Id, IconManifestMapper.ToPackEntity(manifest));
				foreach (var icon in IconManifestMapper.ToIconEntities(manifest))
				{
					_icons.TryAdd(icon.Id, icon);
				}
			}

			RebuildContentIndexes();
			_isInitialized = true;
		}
		finally
		{
			_initializationLock.Release();
			_logger.Information("Initialized icon pack cache with {PackCount} pack(s) and {IconCount} icon(s)",
				_packs.Count,
				_icons.Count);
		}
	}


	public IconPackEntity? GetPackById(Guid id)
	{
		_packs.TryGetValue(id, out var pack);
		return pack;
	}

	public List<IconPackEntity> GetAllPacks() => _packs.Values.ToList();

	public IconPackEntity? GetDefaultPack() => _packs.Values.FirstOrDefault(p => p.IsDefault);

	public async Task AddOrUpdatePack(IconPackEntity pack)
	{
		await _updateLock.WaitAsync();
		try
		{
			pack.UpdatedAt = DateTime.UtcNow;
			_packs.AddOrUpdate(pack.Id, pack, (_, _) => pack);
			Persist(pack.Id);
		}
		finally
		{
			_updateLock.Release();
		}
	}

	public async Task RemovePack(Guid id)
	{
		await _updateLock.WaitAsync();
		try
		{
			_packs.TryRemove(id, out _);
			foreach (var iconId in _icons.Values.Where(i => i.PackId == id).Select(i => i.Id).ToList())
			{
				_icons.TryRemove(iconId, out _);
			}

			RebuildContentIndexes();
			_dirtyPacks.TryRemove(id, out _);
			_store.Delete(id);
			_logger.Information("Removed icon pack with id {PackId}", id);
		}
		finally
		{
			_updateLock.Release();
		}
	}


	public IconEntity? GetIconById(Guid iconId)
	{
		_icons.TryGetValue(iconId, out var icon);
		return icon;
	}

	public List<IconEntity> GetIconsByPackId(Guid packId)
		=> _icons.Values
			.Where(i => i.PackId == packId)
			.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
			.ToList();

	public List<IconEntity> GetIconsByBatchId(Guid batchId)
		=> _icons.Values.Where(i => i.ImportBatchId == batchId).ToList();

	public List<IconEntity> GetIconsByState(params IconProcessingState[] states)
		=> _icons.Values.Where(i => states.Contains(i.ProcessingState)).ToList();

	public int GetIconCount(Guid packId) => _icons.Values.Count(i => i.PackId == packId);

	public IconEntity? FindBySourceContentHash(SourceContentHash hash, Guid? withinPackId = null)
		=> ResolveIndexed(_sourceIndex, hash.Value, withinPackId);

	public IconEntity? FindByMasterContentHash(MasterContentHash hash, Guid? withinPackId = null)
		=> ResolveIndexed(_masterIndex, hash.Value, withinPackId);

	public List<IconEntity> GetIconsMissingMasterContentHash()
		=> _icons.Values
			.Where(icon => icon.ProcessingState == IconProcessingState.Ready && icon.MasterContentHash is null)
			.OrderBy(icon => icon.CreatedAt)
			.ThenBy(icon => icon.Id)
			.ToList();

	public async Task AddIcons(Guid packId, IReadOnlyList<IconEntity> icons)
	{
		await _updateLock.WaitAsync();
		try
		{
			var clean = true;
			foreach (var icon in icons)
			{
				icon.PackId = packId;
				_icons.AddOrUpdate(icon.Id, icon, (_, _) => icon);
				clean &= IndexIcon(icon);
			}

			if (!clean)
			{
				RebuildContentIndexes();
			}

			Persist(packId);
		}
		finally
		{
			_updateLock.Release();
		}
	}

	public async Task UpdateIcon(IconEntity icon)
	{
		await _updateLock.WaitAsync();
		try
		{
			icon.UpdatedAt = DateTime.UtcNow;
			_icons.AddOrUpdate(icon.Id, icon, (_, _) => icon);
			if (!IndexIcon(icon))
			{
				RebuildContentIndexes();
			}

			_dirtyPacks.TryAdd(icon.PackId, 0);
			_flushTimer.Change(_flushDelay, Timeout.InfiniteTimeSpan);
		}
		finally
		{
			_updateLock.Release();
		}
	}

	public async Task RemoveIcon(Guid iconId)
	{
		await _updateLock.WaitAsync();
		try
		{
			if (_icons.TryRemove(iconId, out var icon))
			{
				RebuildContentIndexes();
				Persist(icon.PackId);
			}
		}
		finally
		{
			_updateLock.Release();
		}
	}

	public async Task RemoveIcons(Guid packId, IReadOnlyList<Guid> iconIds)
	{
		await _updateLock.WaitAsync();
		try
		{
			var removedAny = false;
			foreach (var iconId in iconIds)
			{
				removedAny |= _icons.TryRemove(iconId, out _);
			}

			if (removedAny)
			{
				RebuildContentIndexes();
				Persist(packId);
			}
		}
		finally
		{
			_updateLock.Release();
		}
	}

	public async Task FlushPendingWrites()
	{
		await _updateLock.WaitAsync();
		try
		{
			foreach (var packId in _dirtyPacks.Keys.ToList())
			{
				_dirtyPacks.TryRemove(packId, out _);
				Persist(packId);
			}
		}
		finally
		{
			_updateLock.Release();
		}
	}

	private void OnFlushTimer(object? state)
	{
		_ = FlushPendingWritesSafe();
	}

	private async Task FlushPendingWritesSafe()
	{
		try
		{
			await FlushPendingWrites();
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Failed to flush pending icon pack manifests");
		}
	}


	private void RebuildContentIndexes()
	{
		_sourceIndex.Clear();
		_masterIndex.Clear();
		_indexedHashes.Clear();
		foreach (var icon in _icons.Values.OrderBy(icon => icon.CreatedAt).ThenBy(icon => icon.Id))
		{
			IndexIcon(icon);
		}
	}

	private bool IndexIcon(IconEntity icon)
	{
		// Pending and Failed icons stay out: an import must never be handed an id whose image cannot be
		// served, and a failed icon would otherwise poison every later import of the same bytes.
		var indexable = icon.ProcessingState == IconProcessingState.Ready;
		var entry = new IndexedHashes(icon.PackId,
			indexable ? icon.SourceContentHash : null,
			indexable ? icon.MasterContentHash : null);

		var previous = _indexedHashes.GetValueOrDefault(icon.Id);
		if (previous == entry)
		{
			return true;
		}

		var clean = previous == default;
		if (entry.Source is null && entry.Master is null)
		{
			_indexedHashes.TryRemove(icon.Id, out _);
			return clean;
		}

		_sourceIndex.Add(entry.PackId, entry.Source, icon.Id);
		_masterIndex.Add(entry.PackId, entry.Master, icon.Id);
		_indexedHashes[icon.Id] = entry;
		return clean;
	}

	private void UnindexIcon(Guid iconId)
	{
		if (!_indexedHashes.TryRemove(iconId, out var previous))
		{
			return;
		}

		_sourceIndex.Remove(previous.PackId, previous.Source, iconId);
		_masterIndex.Remove(previous.PackId, previous.Master, iconId);
	}

	private IconEntity? ResolveIndexed(ContentIndex index, string hash, Guid? withinPackId)
	{
		var iconId = index.Find(hash, withinPackId);
		if (iconId is null || !_icons.TryGetValue(iconId.Value, out var icon))
		{
			return null;
		}

		if (icon.ProcessingState != IconProcessingState.Ready)
		{
			return null;
		}

		// An icon whose file vanished underneath us must not be handed out as a usable reference. Evicting it
		// here lets the caller fall through to a normal import instead of referencing an image that 404s.
		using var master = _iconStorage.OpenVariant(icon.PackId, icon.Id, IconVariants.Master);
		if (master is null)
		{
			_logger.Warning("Evicting icon {IconId} from the content index: its master file is missing", icon.Id);
			UnindexIcon(icon.Id);
			return null;
		}

		return icon;
	}

	private sealed class ContentIndex
	{
		private readonly ConcurrentDictionary<string, Guid> _global = new(StringComparer.Ordinal);
		private readonly ConcurrentDictionary<(Guid PackId, string Hash), Guid> _perPack = new();

		public Guid? Find(string hash, Guid? withinPackId)
		{
			if (withinPackId is null)
			{
				return _global.TryGetValue(hash, out var id) ? id : null;
			}

			return _perPack.TryGetValue((withinPackId.Value, hash), out var scoped) ? scoped : null;
		}

		public void Add(Guid packId, string? hash, Guid iconId)
		{
			if (hash is null)
			{
				return;
			}

			_global.TryAdd(hash, iconId);
			_perPack.TryAdd((packId, hash), iconId);
		}

		public void Remove(Guid packId, string? hash, Guid iconId)
		{
			if (hash is null)
			{
				return;
			}

			if (_global.TryGetValue(hash, out var owner) && owner == iconId)
			{
				_global.TryRemove(hash, out _);
			}

			if (_perPack.TryGetValue((packId, hash), out var scopedOwner) && scopedOwner == iconId)
			{
				_perPack.TryRemove((packId, hash), out _);
			}
		}

		public void Clear()
		{
			_global.Clear();
			_perPack.Clear();
		}
	}

	private readonly record struct IndexedHashes(Guid PackId, string? Source, string? Master);

	private void Persist(Guid packId)
	{
		if (!_packs.TryGetValue(packId, out var pack))
		{
			_logger.Warning("Skipped persisting unknown icon pack {PackId}", packId);
			return;
		}

		_dirtyPacks.TryRemove(packId, out _);
		var icons = _icons.Values.Where(i => i.PackId == packId);
		_store.Save(IconManifestMapper.ToManifest(pack, icons));
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_flushTimer.Dispose();
		try
		{
			FlushPendingWrites().GetAwaiter().GetResult();
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Failed to flush icon pack manifests during shutdown");
		}

		_initializationLock.Dispose();
		_updateLock.Dispose();
		_disposed = true;
	}
}
