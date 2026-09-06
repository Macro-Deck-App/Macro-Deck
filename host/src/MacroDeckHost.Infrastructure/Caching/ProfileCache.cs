using System.Collections.Concurrent;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Profiles;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using Serilog;

namespace MacroDeckHost.Infrastructure.Caching;

public sealed class ProfileCache : IProfileCache, IDisposable
{
	private readonly IProfileStore _store;
	private readonly ILogger _logger;
	private readonly IFontCatalog _fontCatalog;
	private readonly ConcurrentDictionary<Guid, ProfileEntity> _profiles = new();
	private readonly ConcurrentDictionary<Guid, FolderEntity> _folders = new();
	private readonly SemaphoreSlim _initializationLock = new(1, 1);
	private readonly SemaphoreSlim _updateLock = new(1, 1);
	private bool _isInitialized;
	private bool _disposed;

	public bool HadUnreadableProfiles { get; private set; }

	// Optional so the many existing callers that construct a ProfileCache without a font concern keep
	// compiling; production resolution always supplies the registered IFontCatalog regardless of this
	// default, since the DI container fills a registered parameter type before falling back to it.
	public ProfileCache(IProfileStore store, ILogger logger, IFontCatalog? fontCatalog = null)
	{
		_store = store;
		_logger = logger;
		_fontCatalog = fontCatalog ?? NullFontCatalog.Instance;
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

			var loadResult = _store.LoadAll();
			HadUnreadableProfiles = loadResult.UnreadableCount > 0;

			foreach (var file in loadResult.Profiles)
			{
				var profile = ProfileFileMapper.ToProfileEntity(file);
				var folders = ProfileFileMapper.ToFolderEntities(file).ToList();
				var repaired = StartFolderInvariant.Normalize(profile, folders);
				// Only on load, never from NormalizeStartFolder - otherwise a folder deliberately sized to
				// match the profile default would be un-pinned on every later save.
				repaired |= GridInheritanceMigration.Normalize(profile, folders);
				repaired |= WidgetFontFaceMigration.Normalize(folders, _fontCatalog);
				repaired |= WidgetIconReferenceMigration.Normalize(folders);

				_profiles.TryAdd(file.Id, profile);
				foreach (var folder in folders)
				{
					_folders.TryAdd(folder.Id, folder);
				}

				if (repaired)
				{
					TryPersist(profile.Id);
				}
			}

			_isInitialized = true;
		}
		finally
		{
			_initializationLock.Release();
			_logger.Information("Initialized profile cache with {ProfileCount} profile(s) and {FolderCount} folder(s)",
				_profiles.Count,
				_folders.Count);
		}
	}


	public ProfileEntity? GetById(Guid id)
	{
		_profiles.TryGetValue(id, out var profile);
		return profile;
	}

	public List<ProfileEntity> GetAll() => _profiles.Values.ToList();

	public async Task AddOrUpdate(ProfileEntity profile)
	{
		await _updateLock.WaitAsync();
		try
		{
			_profiles.AddOrUpdate(profile.Id, profile, (_, _) => profile);
			TryPersist(profile.Id);
		}
		finally
		{
			_updateLock.Release();
		}
	}

	public async Task AddOrUpdateAggregate(ProfileEntity profile, IReadOnlyCollection<FolderEntity> folders)
	{
		await _updateLock.WaitAsync();
		try
		{
			// A profile-archive import lands here too, and unlike a load from disk it never goes through
			// InitializeCache - the archive may have been exported by an older host, so it can still
			// carry legacy font fields that only this host's catalog can resolve.
			WidgetFontFaceMigration.Normalize(folders, _fontCatalog);
			WidgetIconReferenceMigration.Normalize(folders);

			_profiles.AddOrUpdate(profile.Id, profile, (_, _) => profile);
			foreach (var folder in folders)
			{
				_folders.AddOrUpdate(folder.Id, folder, (_, _) => folder);
			}

			NormalizeStartFolder(profile.Id);
			TryPersist(profile.Id);
		}
		finally
		{
			_updateLock.Release();
		}
	}

	public async Task Remove(Guid id)
	{
		await _updateLock.WaitAsync();
		try
		{
			_profiles.TryRemove(id, out _);
			foreach (var folderId in _folders.Values.Where(f => f.ProfileId == id).Select(f => f.Id).ToList())
			{
				_folders.TryRemove(folderId, out _);
			}

			_store.Delete(id);
			_logger.Information("Removed profile with id {ProfileId}", id);
		}
		finally
		{
			_updateLock.Release();
		}
	}


	public FolderEntity? GetFolderById(Guid id)
	{
		_folders.TryGetValue(id, out var folder);
		return folder;
	}

	public List<FolderEntity> GetAllFolders() => _folders.Values.ToList();

	public List<FolderEntity> GetFoldersByParentId(Guid? parentId)
		=> _folders.Values
			.Where(f => f.ParentId == parentId)
			.OrderBy(f => f.Order)
			.ThenBy(f => f.Id)
			.ToList();

	public List<FolderEntity> GetFoldersByProfileId(Guid profileId)
		=> _folders.Values
			.Where(f => f.ProfileId == profileId)
			.OrderBy(f => f.Order)
			.ThenBy(f => f.Id)
			.ToList();

	public async Task AddOrUpdateFolder(FolderEntity folder)
	{
		await _updateLock.WaitAsync();
		try
		{
			// A folder-archive import lands here with its widgets already attached and never goes
			// through InitializeCache - see the note in AddOrUpdateAggregate.
			WidgetFontFaceMigration.Normalize([folder], _fontCatalog);
			WidgetIconReferenceMigration.Normalize([folder]);

			_folders.AddOrUpdate(folder.Id, folder, (_, _) => folder);
			NormalizeStartFolder(folder.ProfileId);
			TryPersist(folder.ProfileId);
		}
		finally
		{
			_updateLock.Release();
		}
	}

	public async Task AddOrUpdateFolders(IReadOnlyCollection<FolderEntity> folders)
	{
		await _updateLock.WaitAsync();
		try
		{
			foreach (var folder in folders)
			{
				_folders.AddOrUpdate(folder.Id, folder, (_, _) => folder);
			}

			foreach (var profileId in folders.Select(folder => folder.ProfileId).Distinct())
			{
				NormalizeStartFolder(profileId);
				TryPersist(profileId);
			}
		}
		finally
		{
			_updateLock.Release();
		}
	}

	public async Task<FolderSubtreeRemoval> RemoveFolderSubtree(Guid rootId)
	{
		await _updateLock.WaitAsync();
		try
		{
			if (!_folders.TryGetValue(rootId, out var root))
			{
				return new FolderSubtreeRemoval(false, [], [], FolderSubtreeRemovalFailure.RootNotFound);
			}

			var profileFolders = _folders.Values.Where(folder => folder.ProfileId == root.ProfileId).ToList();
			var subtree = FolderSubtree.Collect(profileFolders, root);
			var subtreeIds = subtree.Select(folder => folder.Id).ToHashSet();

			if (profileFolders.Count <= subtreeIds.Count)
			{
				return new FolderSubtreeRemoval(false, [], [], FolderSubtreeRemovalFailure.LastFolder);
			}

			var survivingRoots = profileFolders
				.Any(folder => !subtreeIds.Contains(folder.Id) && folder.ParentId is null);
			if (subtree.Any(folder => folder.IsDefault) && !survivingRoots)
			{
				return new FolderSubtreeRemoval(false, [], [], FolderSubtreeRemovalFailure.OnlyRootStartFolder);
			}

			var snapshot = profileFolders
				.Select(folder => (Folder: folder, WasDefault: folder.IsDefault, WasParentId: folder.ParentId))
				.ToList();

			foreach (var id in subtreeIds)
			{
				_folders.TryRemove(id, out _);
			}

			NormalizeStartFolder(root.ProfileId);

			if (TryPersist(root.ProfileId))
			{
				var changedStartFolders = snapshot
					.Where(entry => !subtreeIds.Contains(entry.Folder.Id) && entry.Folder.IsDefault != entry.WasDefault)
					.Select(entry => entry.Folder)
					.ToList();
				return new FolderSubtreeRemoval(true, subtree, changedStartFolders);
			}

			foreach (var entry in snapshot)
			{
				entry.Folder.IsDefault = entry.WasDefault;
				entry.Folder.ParentId = entry.WasParentId;
				_folders.TryAdd(entry.Folder.Id, entry.Folder);
			}

			return new FolderSubtreeRemoval(false, [], [], FolderSubtreeRemovalFailure.PersistFailed);
		}
		finally
		{
			_updateLock.Release();
		}
	}

	public void AddWidget(Guid folderId, WidgetEntity widget)
	{
		_updateLock.Wait();
		try
		{
			if (!_folders.TryGetValue(folderId, out var folder))
			{
				return;
			}

			// A widget-archive import lands here too and never goes through InitializeCache - see the
			// note in AddOrUpdateAggregate.
			WidgetFontFaceMigration.Normalize(widget, _fontCatalog);
			WidgetIconReferenceMigration.Normalize(widget);

			folder.Widgets.Add(widget);
			TryPersist(folder.ProfileId);
		}
		finally
		{
			_updateLock.Release();
		}
	}

	public void AddWidgets(Guid folderId, IReadOnlyList<WidgetEntity> widgets)
	{
		_updateLock.Wait();
		try
		{
			if (!_folders.TryGetValue(folderId, out var folder))
			{
				return;
			}

			foreach (var widget in widgets)
			{
				// A pasted or hand-edited body (CreateWidgetsRequestMessageHandler -> WidgetService.CreateMany)
				// may still carry legacy iconId - the font migration does not hook this path, but an icon
				// left un-migrated here would otherwise never see any migration at all, since AddWidgets is
				// never followed by an individual AddWidget for the same widget.
				WidgetIconReferenceMigration.Normalize(widget);
				folder.Widgets.Add(widget);
			}

			TryPersist(folder.ProfileId);
		}
		finally
		{
			_updateLock.Release();
		}
	}

	public void UpdateWidget(Guid folderId, WidgetEntity widget)
	{
		_updateLock.Wait();
		try
		{
			if (!_folders.TryGetValue(folderId, out var folder))
			{
				return;
			}

			var index = folder.Widgets.FindIndex(w => w.Id == widget.Id);
			if (index < 0)
			{
				return;
			}

			folder.Widgets[index] = widget;
			TryPersist(folder.ProfileId);
		}
		finally
		{
			_updateLock.Release();
		}
	}

	public void UpdateWidgets(Guid folderId, IReadOnlyList<WidgetEntity> widgets)
	{
		_updateLock.Wait();
		try
		{
			if (!_folders.TryGetValue(folderId, out var folder))
			{
				return;
			}

			foreach (var widget in widgets)
			{
				var index = folder.Widgets.FindIndex(w => w.Id == widget.Id);
				if (index < 0)
				{
					continue;
				}

				folder.Widgets[index] = widget;
			}

			TryPersist(folder.ProfileId);
		}
		finally
		{
			_updateLock.Release();
		}
	}

	public void UpdateWidgetPositions(Guid folderId, IReadOnlyList<WidgetPlacement> placements)
	{
		_updateLock.Wait();
		try
		{
			if (!_folders.TryGetValue(folderId, out var folder))
			{
				return;
			}

			foreach (var placement in placements)
			{
				var widget = folder.Widgets.FirstOrDefault(w => w.Id == placement.WidgetId);
				if (widget is null)
				{
					continue;
				}

				widget.PositionX = placement.X;
				widget.PositionY = placement.Y;
				widget.Width = placement.Width;
				widget.Height = placement.Height;
			}

			TryPersist(folder.ProfileId);
		}
		finally
		{
			_updateLock.Release();
		}
	}

	public void RemoveWidget(Guid folderId, Guid widgetId)
	{
		_updateLock.Wait();
		try
		{
			if (!_folders.TryGetValue(folderId, out var folder))
			{
				return;
			}

			folder.Widgets.RemoveAll(w => w.Id == widgetId);
			TryPersist(folder.ProfileId);
		}
		finally
		{
			_updateLock.Release();
		}
	}

	public void RemoveWidgets(Guid folderId, IReadOnlyList<Guid> widgetIds)
	{
		_updateLock.Wait();
		try
		{
			if (!_folders.TryGetValue(folderId, out var folder))
			{
				return;
			}

			var idSet = widgetIds.ToHashSet();
			folder.Widgets.RemoveAll(w => idSet.Contains(w.Id));
			TryPersist(folder.ProfileId);
		}
		finally
		{
			_updateLock.Release();
		}
	}

	public void ReplaceWidgets(Guid folderId, IReadOnlyList<Guid> removeIds, IReadOnlyList<WidgetEntity> addWidgets)
	{
		_updateLock.Wait();
		try
		{
			if (!_folders.TryGetValue(folderId, out var folder))
			{
				return;
			}

			var idSet = removeIds.ToHashSet();
			folder.Widgets.RemoveAll(w => idSet.Contains(w.Id));
			foreach (var widget in addWidgets)
			{
				folder.Widgets.Add(widget);
			}

			TryPersist(folder.ProfileId);
		}
		finally
		{
			_updateLock.Release();
		}
	}

	private bool TryPersist(Guid profileId)
	{
		if (!_profiles.TryGetValue(profileId, out var profile))
		{
			_logger.Warning("Skipped persisting unknown profile {ProfileId}", profileId);
			return true;
		}

		var folders = _folders.Values.Where(f => f.ProfileId == profileId);
		return _store.Save(ProfileFileMapper.ToFile(profile, folders));
	}

	private void NormalizeStartFolder(Guid profileId)
	{
		if (!_profiles.TryGetValue(profileId, out var profile))
		{
			return;
		}

		var folders = _folders.Values.Where(folder => folder.ProfileId == profileId).ToList();
		if (!StartFolderInvariant.Normalize(profile, folders))
		{
			return;
		}

		foreach (var folder in folders)
		{
			_folders.AddOrUpdate(folder.Id, folder, (_, _) => folder);
		}
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_initializationLock.Dispose();
		_updateLock.Dispose();
		_disposed = true;
	}

	private sealed class NullFontCatalog : IFontCatalog
	{
		public static readonly NullFontCatalog Instance = new();

		public IReadOnlyList<FontFaceInfo> GetFaces() => [];

		public byte[]? GetFaceFile(string faceId) => null;
	}
}
