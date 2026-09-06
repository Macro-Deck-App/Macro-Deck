using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Portable;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using Mediator;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Portable;

public sealed class WidgetPortabilityService : IWidgetPortabilityService
{
	private readonly IFolderCache _folderCache;
	private readonly IProfileCache _profileCache;
	private readonly IPortableAssetManager _assetManager;
	private readonly IMediator _mediator;
	private readonly ILogger _logger;

	public WidgetPortabilityService(
		IFolderCache folderCache,
		IProfileCache profileCache,
		IPortableAssetManager assetManager,
		IMediator mediator,
		ILogger logger)
	{
		_folderCache = folderCache;
		_profileCache = profileCache;
		_assetManager = assetManager;
		_mediator = mediator;
		_logger = logger;
	}

	public async Task<Result<byte[], PortabilityError>> Export(Guid folderId,
		IReadOnlyList<Guid> widgetIds,
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

		var requested = new HashSet<Guid>(widgetIds);
		var selected = folder.Widgets.Where(widget => requested.Contains(widget.Id)).ToList();
		if (selected.Count == 0)
		{
			return Result.Fail<byte[], PortabilityError>(PortabilityError.ValidationError, "No widgets selected");
		}

		var minX = selected.Min(widget => widget.PositionX);
		var minY = selected.Min(widget => widget.PositionY);
		var portableWidgets = selected.Select(widget => new PortableWidget
			{
				Type = widget.Type,
				PositionX = widget.PositionX - minX,
				PositionY = widget.PositionY - minY,
				Width = widget.Width,
				Height = widget.Height,
				Data = widget.Data,
				SourceId = widget.Id
			})
			.ToList();

		var assets = await _assetManager.Collect(
			selected.Select(widget => new PortableWidgetSource(widget.Id, widget.Data)).ToList(),
			options,
			cancellationToken);

		var content = PortableContentFactory.FromAssets(PortableArchiveKind.Widgets, assets);
		content.Widgets = portableWidgets;

		var manifest = PortableManifestFactory.Create(PortableArchiveKind.Widgets,
			options,
			content,
			assets,
			folder.Name,
			portableWidgets.Count);
		var bytes = PortableArchive.Write(manifest, content, assets.Files, options.Password);
		return Result.Ok<byte[], PortabilityError>(bytes);
	}

	public async Task<Result<IReadOnlyList<WidgetEntity>, PortabilityError>> Import(Guid folderId,
		int anchorX,
		int anchorY,
		byte[] archiveBytes,
		string? password,
		CancellationToken cancellationToken)
	{
		var outcome = PortableArchive.Read(archiveBytes, password);
		var mapped = PortableReadStatusMapper.ToError(outcome.Status);
		if (mapped is not null)
		{
			return Result.Fail<IReadOnlyList<WidgetEntity>, PortabilityError>(mapped.Value);
		}

		var content = outcome.Content!;
		if (content.Kind != PortableArchiveKind.Widgets || content.Widgets is not { Count: > 0 } widgets)
		{
			return Result.Fail<IReadOnlyList<WidgetEntity>, PortabilityError>(PortabilityError.InvalidArchive,
				"The archive does not contain widgets");
		}

		var folder = _folderCache.GetFolderById(folderId);
		if (folder is null)
		{
			return Result.Fail<IReadOnlyList<WidgetEntity>, PortabilityError>(PortabilityError.NotFound);
		}

		var profileFolders = _folderCache.GetFoldersByProfileId(folder.ProfileId);
		var profile = _profileCache.GetById(folder.ProfileId);
		var effectiveColumns =
			GridInheritance.ResolveColumns(folder, profileFolders, profile?.DefaultColumns ?? GridDefaults.Columns);
		var effectiveRows =
			GridInheritance.ResolveRows(folder, profileFolders, profile?.DefaultRows ?? GridDefaults.Rows);

		var group = widgets
			.Select(widget => new PortableCell(widget.PositionX, widget.PositionY, widget.Width, widget.Height))
			.ToList();
		var occupied = PinnedWidgetLayout
			.DisplayedWidgets(folder, profileFolders)
			.Select(widget => new PortableCell(widget.PositionX, widget.PositionY, widget.Width, widget.Height))
			.ToList();
		var positions =
			PortableWidgetPlacement.Place(group, effectiveColumns, effectiveRows, occupied, anchorX, anchorY);
		if (positions is null)
		{
			return Result.Fail<IReadOnlyList<WidgetEntity>, PortabilityError>(PortabilityError.ValidationError,
				"Not enough free space in the folder");
		}

		try
		{
			var assetIdMap = await _assetManager.Import(content, outcome.Icons, cancellationToken);

			var created = new List<WidgetEntity>();
			var widgetIdMap = new Dictionary<Guid, Guid>();
			for (var i = 0; i < widgets.Count; i++)
			{
				var widget = widgets[i];
				var (x, y) = positions[i];
				var entity = new WidgetEntity
				{
					Id = Guid.NewGuid(),
					FolderId = folderId,
					Type = widget.Type,
					PositionX = x,
					PositionY = y,
					Width = widget.Width,
					Height = widget.Height,
					Data = PortableActionButtonStateNormalizer.Normalize(widget.Type,
						PortableGuidRemapper.Remap(widget.Data, assetIdMap)),
					CreatedAt = DateTime.UtcNow
				};

				_folderCache.AddWidget(folderId, entity);
				created.Add(entity);
				if (widget.SourceId.HasValue)
				{
					widgetIdMap[widget.SourceId.Value] = entity.Id;
				}
			}

			await _assetManager.RestoreWidgetVariables(content, widgetIdMap, cancellationToken);

			foreach (var widget in created)
			{
				await _mediator.Publish(new WidgetCreatedNotification(widget), cancellationToken);
			}

			return Result.Ok<IReadOnlyList<WidgetEntity>, PortabilityError>(created);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.Error(ex, "Failed to import widgets archive into folder {FolderId}", folderId);
			return Result.Fail<IReadOnlyList<WidgetEntity>, PortabilityError>(PortabilityError.StorageFailure);
		}
	}
}
