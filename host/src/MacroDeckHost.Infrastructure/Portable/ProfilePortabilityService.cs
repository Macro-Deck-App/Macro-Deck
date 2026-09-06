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

public sealed class ProfilePortabilityService : IProfilePortabilityService
{
	private readonly IProfileCache _profileCache;
	private readonly IFolderCache _folderCache;
	private readonly IPortableAssetManager _assetManager;
	private readonly IMediator _mediator;
	private readonly ILogger _logger;

	public ProfilePortabilityService(
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

	public Result<string, PortabilityError> GetExportFileName(Guid profileId)
	{
		var profile = _profileCache.GetById(profileId);
		if (profile is null)
		{
			return Result.Fail<string, PortabilityError>(PortabilityError.NotFound);
		}

		var fileName = PortableFileName.ForArchive(profile.Name, "profile", PortableFileExtensions.Profile);
		return Result.Ok<string, PortabilityError>(fileName);
	}

	public async Task<Result<byte[], PortabilityError>> Export(Guid profileId,
		PortableExportOptions options,
		CancellationToken cancellationToken)
	{
		if (options.Validate() is { } invalid)
		{
			return Result.Fail<byte[], PortabilityError>(invalid);
		}

		var profile = _profileCache.GetById(profileId);
		if (profile is null)
		{
			return Result.Fail<byte[], PortabilityError>(PortabilityError.NotFound);
		}

		var folders = _folderCache.GetFoldersByProfileId(profileId);
		var profileFile = ProfileFileMapper.ToFile(profile, folders);
		// The start-folder invariant belongs to the target profile. The portable archive deliberately
		// excludes this local aggregate state instead of making a template select a folder on import.
		foreach (var folder in profileFile.Folders)
		{
			folder.IsDefault = false;
			folder.FocusRules = null;
		}

		var widgets = folders.SelectMany(folder => folder.Widgets).ToList();
		var assets = await _assetManager.Collect(
			widgets.Select(widget => new PortableWidgetSource(widget.Id, widget.Data)).ToList(),
			options,
			cancellationToken);

		var content = PortableContentFactory.FromAssets(PortableArchiveKind.Profile, assets);
		content.Profile = profileFile;

		var manifest = PortableManifestFactory.Create(PortableArchiveKind.Profile,
			options,
			content,
			assets,
			profile.Name,
			widgets.Count);
		var bytes = PortableArchive.Write(manifest, content, assets.Files, options.Password);
		return Result.Ok<byte[], PortabilityError>(bytes);
	}

	public async Task<Result<ProfileEntity, PortabilityError>> Import(byte[] archiveBytes,
		string? password,
		CancellationToken cancellationToken)
	{
		var outcome = PortableArchive.Read(archiveBytes, password);
		var mapped = PortableReadStatusMapper.ToError(outcome.Status);
		if (mapped is not null)
		{
			return Result.Fail<ProfileEntity, PortabilityError>(mapped.Value);
		}

		var content = outcome.Content!;
		if (content.Kind != PortableArchiveKind.Profile || content.Profile is null)
		{
			return Result.Fail<ProfileEntity, PortabilityError>(PortabilityError.InvalidArchive,
				"The archive does not contain a profile");
		}

		return await ImportContent(content, outcome.Icons, cancellationToken);
	}

	public async Task<Result<ProfileEntity, PortabilityError>> ImportContent(PortableContent content,
		IReadOnlyList<PortableIconFile> iconFiles,
		CancellationToken cancellationToken)
	{
		if (content.Profile is null)
		{
			return Result.Fail<ProfileEntity, PortabilityError>(PortabilityError.InvalidArchive,
				"The content does not contain a profile");
		}

		try
		{
			var assetIdMap = await _assetManager.Import(content, iconFiles, cancellationToken);
			var mintedIds = MintIds(content.Profile);
			var (profile, folders) = BuildEntities(content.Profile, assetIdMap, mintedIds);

			await _profileCache.AddOrUpdateAggregate(profile, folders);

			await _assetManager.RestoreWidgetVariables(content, mintedIds, cancellationToken);

			await _mediator.Publish(new ProfileCreatedNotification(profile), cancellationToken);
			foreach (var folder in folders)
			{
				await _mediator.Publish(new FolderCreatedNotification(folder), cancellationToken);
				foreach (var widget in folder.Widgets)
				{
					await _mediator.Publish(new WidgetCreatedNotification(widget), cancellationToken);
				}
			}

			return Result.Ok<ProfileEntity, PortabilityError>(profile);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.Error(ex, "Failed to import profile archive");
			return Result.Fail<ProfileEntity, PortabilityError>(PortabilityError.StorageFailure);
		}
	}

	private static Dictionary<Guid, Guid> MintIds(ProfileFile source)
	{
		var idMap = new Dictionary<Guid, Guid> { [source.Id] = Guid.NewGuid() };
		foreach (var folder in source.Folders)
		{
			idMap[folder.Id] = Guid.NewGuid();
			foreach (var widget in folder.Widgets)
			{
				idMap[widget.Id] = Guid.NewGuid();
			}
		}

		return idMap;
	}

	private (ProfileEntity Profile, List<FolderEntity> Folders) BuildEntities(ProfileFile source,
		IReadOnlyDictionary<Guid, Guid> assetIdMap,
		IReadOnlyDictionary<Guid, Guid> mintedIds)
	{
		var importedAt = DateTime.UtcNow;
		var hasCompleteSourceChronology = source.Folders.All(folder => folder.CreatedAt != default);
		var importedStartFolderId = SelectImportedStartFolder(source.Folders, hasCompleteSourceChronology);
		var importedCreatedAt = CreateImportedCreationTimes(source.Folders, importedAt, hasCompleteSourceChronology);
		var idMap = new Dictionary<Guid, Guid>(assetIdMap);
		foreach (var (oldId, newId) in mintedIds)
		{
			idMap[oldId] = newId;
		}

		var newProfileId = idMap[source.Id];

		var existing = _profileCache.GetAll();
		var nextOrder = existing.Count > 0 ? existing.Max(p => p.Order) + 1 : 0;

		var profile = new ProfileEntity
		{
			Id = newProfileId,
			Name = source.Name,
			Order = nextOrder,
			LayoutType = source.LayoutType,
			DefaultRows = source.DefaultRows,
			DefaultColumns = source.DefaultColumns,
			DefaultBackgroundColor = source.DefaultBackgroundColor,
			DefaultWidgetSpacing = source.DefaultWidgetSpacing,
			DefaultWidgetBorderRadius = source.DefaultWidgetBorderRadius,
			CreatedAt = importedAt
		};

		var folders = source.Folders.Select(folder => new FolderEntity
			{
				Id = idMap[folder.Id],
				ProfileId = newProfileId,
				Name = folder.Name,
				ParentId = folder.ParentId is { } parentId && idMap.TryGetValue(parentId, out var mapped)
					? mapped
					: folder.ParentId,
				Order = folder.Order,
				Rows = folder.Rows,
				Columns = folder.Columns,
				BackgroundColor = folder.BackgroundColor,
				WidgetSpacing = folder.WidgetSpacing,
				WidgetBorderRadius = folder.WidgetBorderRadius,
				IsDefault = folder.Id == importedStartFolderId,
				CreatedAt = importedCreatedAt[folder.Id],
				Widgets = folder.Widgets.Select(widget => new WidgetEntity
					{
						Id = idMap[widget.Id],
						FolderId = idMap[folder.Id],
						Type = widget.Type,
						PositionX = widget.PositionX,
						PositionY = widget.PositionY,
						Width = widget.Width,
						Height = widget.Height,
						Data = PortableActionButtonStateNormalizer.Normalize(widget.Type,
							PortableGuidRemapper.Remap(widget.Data, idMap)),
						IsPinned = widget.IsPinned,
						PinScope = widget.PinScope,
						CreatedAt = DateTime.UtcNow
					})
					.ToList()
			})
			.ToList();

		return (profile, folders);
	}

	private static Guid? SelectImportedStartFolder(IReadOnlyCollection<ProfileFolder> folders,
		bool hasCompleteSourceChronology)
	{
		var roots = folders.Where(folder => folder.ParentId is null).ToList();
		if (roots.Count == 0)
		{
			return null;
		}

		return hasCompleteSourceChronology
			? roots.OrderBy(folder => folder.CreatedAt).ThenBy(folder => folder.Id).First().Id
			: roots.OrderBy(folder => folder.Order).ThenBy(folder => folder.Id).First().Id;
	}

	private static Dictionary<Guid, DateTime> CreateImportedCreationTimes(
		IReadOnlyCollection<ProfileFolder> folders,
		DateTime importedAt,
		bool hasCompleteSourceChronology)
	{
		var ordered = hasCompleteSourceChronology
			? folders.OrderBy(folder => folder.CreatedAt).ThenBy(folder => folder.Id).ToList()
			: folders.OrderBy(folder => folder.Order).ThenBy(folder => folder.Id).ToList();
		var createdAt = new Dictionary<Guid, DateTime>(ordered.Count);

		if (!hasCompleteSourceChronology)
		{
			for (var index = 0; index < ordered.Count; index++)
			{
				createdAt[ordered[index].Id] = importedAt.AddTicks(index);
			}

			return createdAt;
		}

		var last = DateTime.MinValue;
		foreach (var folder in ordered)
		{
			var timestamp = folder.CreatedAt > last ? folder.CreatedAt : last.AddTicks(1);
			createdAt[folder.Id] = timestamp;
			last = timestamp;
		}

		return createdAt;
	}
}
