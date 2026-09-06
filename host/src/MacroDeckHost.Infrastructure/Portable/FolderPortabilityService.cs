using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Persistence.Profiles;
using MacroDeckHost.Application.Portable;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Caching;
using Mediator;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Portable;

public sealed class FolderPortabilityService : IFolderPortabilityService
{
	private const int MaxGridSize = 512;

	private readonly IProfileCache _profileCache;
	private readonly IFolderCache _folderCache;
	private readonly IPortableAssetManager _assetManager;
	private readonly IMediator _mediator;
	private readonly ILogger _logger;

	public FolderPortabilityService(
		IProfileCache profileCache,
		IFolderCache folderCache,
		IPortableAssetManager assetManager,
		IMediator mediator,
		ILogger logger)
	{
		_profileCache = profileCache;
		_folderCache = folderCache;
		_assetManager = assetManager;
		_mediator = mediator;
		_logger = logger;
	}

	public Result<string, PortabilityError> GetExportFileName(Guid folderId)
	{
		var folder = _folderCache.GetFolderById(folderId);
		if (folder is null)
		{
			return Result.Fail<string, PortabilityError>(PortabilityError.NotFound);
		}

		var fileName = PortableFileName.ForArchive(folder.Name, "folder", PortableFileExtensions.Folder);
		return Result.Ok<string, PortabilityError>(fileName);
	}

	public async Task<Result<byte[], PortabilityError>> Export(Guid folderId,
		PortableExportOptions options,
		CancellationToken cancellationToken)
	{
		if (options.Validate() is { } invalid)
		{
			return Result.Fail<byte[], PortabilityError>(invalid);
		}

		var folder = _folderCache.GetFolderById(folderId);
		if (folder is null)
		{
			return Result.Fail<byte[], PortabilityError>(PortabilityError.NotFound);
		}

		var exported = CollectSubtree(folder, options.IncludeSubfolders);
		var portableFolders = exported.Select(ProfileFileMapper.ToFolderFile).ToList();

		var profile = _profileCache.GetById(folder.ProfileId);
		var profileFolders = _folderCache.GetFoldersByProfileId(folder.ProfileId);
		var profileDefaultRows = profile?.DefaultRows ?? GridDefaults.Rows;
		var profileDefaultColumns = profile?.DefaultColumns ?? GridDefaults.Columns;
		for (var i = 0; i < exported.Count; i++)
		{
			portableFolders[i].Rows = GridInheritance.ResolveRows(exported[i], profileFolders, profileDefaultRows);
			portableFolders[i].Columns =
				GridInheritance.ResolveColumns(exported[i], profileFolders, profileDefaultColumns);
		}

		portableFolders[0].ParentId = null;
		portableFolders[0].Order = 0;
		// A start marker is profile-local and therefore must not leave the source profile in a folder
		// archive. False is omitted by the portable JSON contract.
		foreach (var portableFolder in portableFolders)
		{
			portableFolder.IsDefault = false;
			portableFolder.FocusRules = null;
		}

		var widgets = exported.SelectMany(f => f.Widgets).ToList();
		var assets = await _assetManager.Collect(
			widgets.Select(widget => new PortableWidgetSource(widget.Id, widget.Data)).ToList(),
			options,
			cancellationToken);

		var content = PortableContentFactory.FromAssets(PortableArchiveKind.Folder, assets);
		content.Folders = portableFolders;

		var manifest = PortableManifestFactory.Create(PortableArchiveKind.Folder,
			options,
			content,
			assets,
			folder.Name,
			widgets.Count);
		var bytes = PortableArchive.Write(manifest, content, assets.Files, options.Password);
		return Result.Ok<byte[], PortabilityError>(bytes);
	}

	public async Task<Result<IReadOnlyList<FolderEntity>, PortabilityError>> Import(Guid profileId,
		Guid? parentFolderId,
		byte[] archiveBytes,
		string? password,
		CancellationToken cancellationToken)
	{
		var outcome = PortableArchive.Read(archiveBytes, password);
		var mapped = PortableReadStatusMapper.ToError(outcome.Status);
		if (mapped is not null)
		{
			return Fail(mapped.Value);
		}

		var content = outcome.Content!;
		if (content.Kind != PortableArchiveKind.Folder || content.Folders is not { Count: > 0 } archived)
		{
			return Fail(PortabilityError.InvalidArchive, "The archive does not contain a folder");
		}

		var targetProfile = _profileCache.GetById(profileId);
		if (targetProfile is null)
		{
			return Fail(PortabilityError.NotFound, "Profile not found");
		}

		if (parentFolderId.HasValue)
		{
			var parent = _folderCache.GetFolderById(parentFolderId.Value);
			if (parent is null || parent.ProfileId != profileId)
			{
				return Fail(PortabilityError.ValidationError, "The target folder no longer exists");
			}
		}

		var ordered = OrderFromRoot(archived);
		if (ordered is null)
		{
			return Fail(PortabilityError.InvalidArchive, "The archived folder tree is malformed");
		}

		if (ordered.Any(folder => folder.Rows is null or < 1 or > MaxGridSize ||
			folder.Columns is null or < 1 or > MaxGridSize))
		{
			return Fail(PortabilityError.InvalidArchive, "The archived folder grid is out of range");
		}

		var mintedIds = MintIds(ordered);
		var idMap = new Dictionary<Guid, Guid>(mintedIds);
		var folders = BuildFolders(ordered, profileId, parentFolderId, idMap);

		// The grids and positions are settled before anything is created: the profile's pinned widgets
		// render in every folder they reach, so an imported folder has to leave their cells free - and a
		// folder that cannot is a failed import, not a half-imported tree.
		var profileFolders = _folderCache.GetFoldersByProfileId(profileId);
		var profileDefaultRows = targetProfile.DefaultRows;
		var profileDefaultColumns = targetProfile.DefaultColumns;
		var foldersForReach = profileFolders.Concat(folders).ToList();
		foreach (var folder in folders)
		{
			var effectiveColumns = GridInheritance.ResolveColumns(folder, foldersForReach, profileDefaultColumns);
			var effectiveRows = GridInheritance.ResolveRows(folder, foldersForReach, profileDefaultRows);
			PinnedWidgetLayout.GrowToFitPinned(folder, foldersForReach, effectiveColumns, effectiveRows);

			if (!TryPlaceWidgets(folder, foldersForReach, folder.Columns!.Value, folder.Rows!.Value))
			{
				return Fail(PortabilityError.ValidationError,
					$"'{folder.Name}' does not fit into the profile's grid alongside its pinned widgets");
			}
		}

		try
		{
			foreach (var (oldId, newId) in await _assetManager.Import(content, outcome.Icons, cancellationToken))
			{
				idMap[oldId] = newId;
			}

			foreach (var widget in folders.SelectMany(folder => folder.Widgets))
			{
				widget.Data = PortableActionButtonStateNormalizer.Normalize(widget.Type,
					PortableGuidRemapper.Remap(widget.Data, idMap));
			}

			foreach (var folder in folders)
			{
				await _folderCache.AddOrUpdate(folder);
			}

			// The minted ids only, never idMap: that one also carries imported asset ids, whose targets are
			// pre-existing local icons and scripts once dedup hits. Handing those over would let a
			// hand-edited archive name an icon id and have its variables land on that icon's id, where no
			// widget deletion will ever reap them.
			await _assetManager.RestoreWidgetVariables(content, mintedIds, cancellationToken);

			foreach (var folder in folders)
			{
				await _mediator.Publish(new FolderCreatedNotification(folder), cancellationToken);
				foreach (var widget in folder.Widgets)
				{
					await _mediator.Publish(new WidgetCreatedNotification(widget), cancellationToken);
				}
			}

			return Result.Ok<IReadOnlyList<FolderEntity>, PortabilityError>(folders);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.Error(ex, "Failed to import folder archive into profile {ProfileId}", profileId);
			return Fail(PortabilityError.StorageFailure);
		}
	}

	private List<FolderEntity> CollectSubtree(FolderEntity root, bool includeSubfolders)
	{
		if (!includeSubfolders)
		{
			return [root];
		}

		return FolderSubtree.Collect(_folderCache.GetFoldersByProfileId(root.ProfileId), root);
	}

	private static List<ProfileFolder>? OrderFromRoot(List<ProfileFolder> archived)
	{
		var roots = archived.Where(folder => folder.ParentId is null).ToList();
		if (roots.Count != 1)
		{
			return null;
		}

		var byParent = archived.Where(folder => folder.ParentId.HasValue)
			.GroupBy(folder => folder.ParentId!.Value)
			.ToDictionary(group => group.Key, group => group.OrderBy(folder => folder.Order).ToList());

		var ordered = new List<ProfileFolder>();
		var visited = new HashSet<Guid> { roots[0].Id };
		var queue = new Queue<ProfileFolder>();
		queue.Enqueue(roots[0]);
		while (queue.Count > 0)
		{
			var folder = queue.Dequeue();
			ordered.Add(folder);
			if (!byParent.TryGetValue(folder.Id, out var children))
			{
				continue;
			}

			foreach (var child in children.Where(child => visited.Add(child.Id)))
			{
				queue.Enqueue(child);
			}
		}

		return ordered.Count == archived.Count ? ordered : null;
	}

	private static Dictionary<Guid, Guid> MintIds(List<ProfileFolder> ordered)
	{
		var idMap = new Dictionary<Guid, Guid>();
		foreach (var folder in ordered)
		{
			idMap[folder.Id] = Guid.NewGuid();
			foreach (var widget in folder.Widgets)
			{
				idMap[widget.Id] = Guid.NewGuid();
			}
		}

		return idMap;
	}

	private List<FolderEntity> BuildFolders(List<ProfileFolder> ordered,
		Guid profileId,
		Guid? parentFolderId,
		Dictionary<Guid, Guid> idMap)
	{
		var siblings = _folderCache.GetFoldersByProfileId(profileId)
			.Where(folder => folder.ParentId == parentFolderId)
			.ToList();
		var rootOrder = siblings.Count > 0 ? siblings.Max(folder => folder.Order) + 1 : 0;

		return ordered.Select((source, index) => new FolderEntity
			{
				Id = idMap[source.Id],
				ProfileId = profileId,
				Name = source.Name,
				ParentId = source.ParentId is { } sourceParentId ? idMap[sourceParentId] : parentFolderId,
				Order = index == 0 ? rootOrder : source.Order,
				Rows = source.Rows,
				Columns = source.Columns,
				BackgroundColor = source.BackgroundColor,
				WidgetSpacing = source.WidgetSpacing,
				WidgetBorderRadius = source.WidgetBorderRadius,
				IsDefault = false,
				// Imported as-is, never validated against the live catalog: an archive may well name a
				// view whose integration is not installed here yet, and dropping it would lose the very
				// configuration reinstalling that integration is meant to restore.
				ViewId = source.ViewId,
				ViewConfiguration = source.ViewConfiguration,
				CreatedAt = DateTime.UtcNow,
				Widgets = source.Widgets.Select(widget => new WidgetEntity
					{
						Id = idMap[widget.Id],
						FolderId = idMap[source.Id],
						Type = widget.Type,
						PositionX = widget.PositionX,
						PositionY = widget.PositionY,
						Width = widget.Width,
						Height = widget.Height,
						Data = widget.Data,
						IsPinned = false,
						PinScope = PinScope.Profile,
						CreatedAt = DateTime.UtcNow
					})
					.ToList()
			})
			.ToList();
	}

	private static bool TryPlaceWidgets(
		FolderEntity folder,
		IReadOnlyList<FolderEntity> profileFolders,
		int effectiveColumns,
		int effectiveRows)
	{
		var group = folder.Widgets
			.Select(widget => new PortableCell(widget.PositionX, widget.PositionY, widget.Width, widget.Height))
			.ToList();
		var occupied = PinnedWidgetLayout.PinnedWidgetsOutside(folder, profileFolders)
			.Select(widget => new PortableCell(widget.PositionX, widget.PositionY, widget.Width, widget.Height))
			.ToList();

		var positions = PortableWidgetPlacement.Place(group, effectiveColumns, effectiveRows, occupied, 0, 0);
		if (positions is null)
		{
			return false;
		}

		for (var i = 0; i < folder.Widgets.Count; i++)
		{
			folder.Widgets[i].PositionX = positions[i].X;
			folder.Widgets[i].PositionY = positions[i].Y;
		}

		return true;
	}

	private static Result<IReadOnlyList<FolderEntity>, PortabilityError> Fail(PortabilityError error,
		string? message = null)
		=> message is null
			? Result.Fail<IReadOnlyList<FolderEntity>, PortabilityError>(error)
			: Result.Fail<IReadOnlyList<FolderEntity>, PortabilityError>(error, message);
}
