using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using Mediator;

namespace MacroDeckHost.Application.Services;

public class WidgetService : IWidgetService
{
	private static readonly IReadOnlySet<Guid> EmptyRemovedIds = new HashSet<Guid>();

	private readonly IFolderCache _folderCache;
	private readonly IProfileCache _profileCache;
	private readonly IMediator _mediator;
	private readonly IWidgetSecretScrubber _widgetSecretScrubber;
	private readonly IWidgetSecretCloner _widgetSecretCloner;
	private readonly IWidgetVariableCloner _widgetVariableCloner;

	public WidgetService(
		IFolderCache folderCache,
		IProfileCache profileCache,
		IMediator mediator,
		IWidgetSecretScrubber widgetSecretScrubber,
		IWidgetSecretCloner widgetSecretCloner,
		IWidgetVariableCloner widgetVariableCloner)
	{
		_folderCache = folderCache;
		_profileCache = profileCache;
		_mediator = mediator;
		_widgetSecretScrubber = widgetSecretScrubber;
		_widgetSecretCloner = widgetSecretCloner;
		_widgetVariableCloner = widgetVariableCloner;
	}

	public async Task<Result<WidgetEntity, WidgetError>> Create(
		Guid folderId,
		WidgetEntity widget,
		Guid? sourceWidgetId = null)
	{
		var folder = _folderCache.GetFolderById(folderId);
		if (folder is null)
		{
			return Result.Fail<WidgetEntity, WidgetError>(WidgetError.FolderNotFound, "Folder not found");
		}

		widget.FolderId = folderId;
		widget.CreatedAt = DateTime.UtcNow;
		widget.IsPinned = false;
		widget.PinScope = PinScope.Profile;

		if (widget.Id == Guid.Empty)
		{
			widget.Id = Guid.NewGuid();
		}

		var profileFolders = _folderCache.GetFoldersByProfileId(folder.ProfileId);
		var (effectiveColumns, effectiveRows) = ResolveGrid(folder, profileFolders);

		var placement = new WidgetPlacement(widget.Id, widget.PositionX, widget.PositionY, widget.Width, widget.Height);
		if (WidgetLayoutValidation.OutOfBounds(placement, effectiveColumns, effectiveRows))
		{
			return Result.Fail<WidgetEntity, WidgetError>(WidgetError.ValidationError,
				"Placement outside the folder grid");
		}

		var foreignPinned = PinnedWidgetLayout
			.PinnedWidgetsOutside(folder, profileFolders)
			.ToList();
		if (foreignPinned.Any(pinned => PinnedWidgetLayout.Overlaps(widget, pinned)))
		{
			return Result.Fail<WidgetEntity, WidgetError>(WidgetError.PositionOccupied,
				"The position is occupied by a pinned widget");
		}

		if (WidgetLayoutValidation.HasOverlap(folder, foreignPinned, [], [placement], EmptyRemovedIds))
		{
			return Result.Fail<WidgetEntity, WidgetError>(WidgetError.PositionOccupied,
				"Placements overlap the final layout");
		}

		// A created widget must own its secrets: clone any referenced secrets (e.g. when pasting a
		// copied widget) so deleting the source later cannot scrub a secret the new widget still uses.
		widget.Data = await _widgetSecretCloner.CloneReferencedSecrets(widget.Data);

		_folderCache.AddWidget(folderId, widget);

		if (sourceWidgetId is { } source)
		{
			await _widgetVariableCloner.Clone(source, widget.Id);
		}

		await _mediator.Publish(new WidgetCreatedNotification(widget));

		return Result.Ok<WidgetEntity, WidgetError>(widget);
	}

	public async Task<Result<WidgetEntity, WidgetError>> Update(WidgetEntity widget)
	{
		var folder = _folderCache.GetFolderById(widget.FolderId);
		if (folder is null)
		{
			return Result.Fail<WidgetEntity, WidgetError>(WidgetError.FolderNotFound, "Folder not found");
		}

		var existing = folder.Widgets.FirstOrDefault(w => w.Id == widget.Id);
		if (existing is not null)
		{
			widget.IsPinned = existing.IsPinned;
			widget.PinScope = existing.PinScope;

			// Only a geometry change is validated. A widget stranded outside the grid by a later grid
			// shrink has to keep accepting the data and appearance writes that reach this method from
			// the runtime paths and from IWidgetApi.ApplyAsync, all of which pass its rectangle back
			// unchanged - it only has to be a legal placement once somebody moves or resizes it.
			if (!SameRect(existing, widget))
			{
				if (existing.IsPinned)
				{
					return Result.Fail<WidgetEntity, WidgetError>(WidgetError.WidgetPinned,
						"A pinned widget cannot be moved or resized");
				}

				var profileFolders = _folderCache.GetFoldersByProfileId(folder.ProfileId);
				var (effectiveColumns, effectiveRows) = ResolveGrid(folder, profileFolders);

				var placement = new WidgetPlacement(widget.Id,
					widget.PositionX,
					widget.PositionY,
					widget.Width,
					widget.Height);
				if (WidgetLayoutValidation.OutOfBounds(placement, effectiveColumns, effectiveRows))
				{
					return Result.Fail<WidgetEntity, WidgetError>(WidgetError.ValidationError,
						"Placement outside the folder grid");
				}

				var foreignPinned = PinnedWidgetLayout
					.PinnedWidgetsOutside(folder, profileFolders)
					.ToList();
				if (WidgetLayoutValidation.HasOverlap(folder, foreignPinned, [placement], [], EmptyRemovedIds))
				{
					return Result.Fail<WidgetEntity, WidgetError>(WidgetError.PositionOccupied,
						"Placements overlap the final layout");
				}
			}
		}

		// Only ever claimed unchanged from two distinct snapshots: a caller that loaded the stored entity,
		// mutated it and handed the same instance back would otherwise be comparing the new data with
		// itself, and a real reconfiguration would look like a move.
		var dataChanged = existing is null ||
			ReferenceEquals(existing, widget) ||
			!string.Equals(existing.Data, widget.Data, StringComparison.Ordinal);

		_folderCache.UpdateWidget(widget.FolderId, widget);

		await _mediator.Publish(new WidgetUpdatedNotification(widget, dataChanged));

		return Result.Ok<WidgetEntity, WidgetError>(widget);
	}

	public async Task<Result<List<WidgetEntity>, WidgetError>> UpdatePositions(
		Guid folderId,
		IReadOnlyList<WidgetPlacement> placements)
	{
		var folder = _folderCache.GetFolderById(folderId);
		if (folder is null)
		{
			return Result.Fail<List<WidgetEntity>, WidgetError>(WidgetError.FolderNotFound, "Folder not found");
		}

		if (placements.Count == 0)
		{
			return Result.Ok<List<WidgetEntity>, WidgetError>([]);
		}

		if (placements.Select(p => p.WidgetId).Distinct().Count() != placements.Count)
		{
			return Result.Fail<List<WidgetEntity>, WidgetError>(WidgetError.ValidationError,
				"Duplicate widget ids in placement batch");
		}

		var profileFolders = _folderCache.GetFoldersByProfileId(folder.ProfileId);
		var (effectiveColumns, effectiveRows) = ResolveGrid(folder, profileFolders);

		var widgetsById = folder.Widgets.ToDictionary(w => w.Id);
		foreach (var placement in placements)
		{
			if (!widgetsById.TryGetValue(placement.WidgetId, out var target))
			{
				return Result.Fail<List<WidgetEntity>, WidgetError>(WidgetError.NotFound, "Widget not found");
			}

			if (target.IsPinned)
			{
				return Result.Fail<List<WidgetEntity>, WidgetError>(WidgetError.WidgetPinned,
					"A pinned widget cannot be moved or resized");
			}

			if (WidgetLayoutValidation.OutOfBounds(placement, effectiveColumns, effectiveRows))
			{
				return Result.Fail<List<WidgetEntity>, WidgetError>(WidgetError.ValidationError,
					"Placement outside the folder grid");
			}
		}

		var foreignPinned = PinnedWidgetLayout
			.PinnedWidgetsOutside(folder, profileFolders)
			.ToList();

		if (WidgetLayoutValidation.HasOverlap(folder, foreignPinned, placements, [], EmptyRemovedIds))
		{
			return Result.Fail<List<WidgetEntity>, WidgetError>(WidgetError.PositionOccupied,
				"Placements overlap the final layout");
		}

		var sizeChangedIds = placements
			.Where(p =>
			{
				var widget = widgetsById[p.WidgetId];
				return widget.Width != p.Width || widget.Height != p.Height;
			})
			.Select(p => p.WidgetId)
			.ToHashSet();

		_folderCache.UpdateWidgetPositions(folderId, placements);

		var updatedWidgets = placements.Select(p => widgetsById[p.WidgetId]).ToList();

		await _mediator.Publish(new WidgetPositionsUpdatedNotification(folderId, updatedWidgets, sizeChangedIds));

		return Result.Ok<List<WidgetEntity>, WidgetError>(updatedWidgets);
	}

	public async Task<Result<WidgetError>> Delete(Guid widgetId, Guid folderId)
	{
		var folder = _folderCache.GetFolderById(folderId);
		if (folder is null)
		{
			return Result.Fail(WidgetError.FolderNotFound, "Folder not found");
		}

		var widget = folder.Widgets.FirstOrDefault(w => w.Id == widgetId);

		_folderCache.RemoveWidget(folderId, widgetId);

		await _widgetSecretScrubber.ScrubReferencedSecrets(widget?.Data);

		await _mediator.Publish(new WidgetDeletedNotification(widgetId, folderId));

		return Result.Ok<WidgetError>();
	}

	public async Task<Result<WidgetEntity, WidgetError>> SetPinned(
		Guid folderId,
		Guid widgetId,
		bool pinned,
		PinScope? scope = null)
	{
		var folder = _folderCache.GetFolderById(folderId);
		if (folder is null)
		{
			return Result.Fail<WidgetEntity, WidgetError>(WidgetError.FolderNotFound, "Folder not found");
		}

		var widget = folder.Widgets.FirstOrDefault(w => w.Id == widgetId);
		if (widget is null)
		{
			return Result.Fail<WidgetEntity, WidgetError>(WidgetError.NotFound, "Widget not found");
		}

		var targetScope = scope ?? widget.PinScope;
		var resultingScope = pinned ? targetScope : PinScope.Profile;

		if (widget.IsPinned == pinned && widget.PinScope == resultingScope)
		{
			return Result.Ok<WidgetEntity, WidgetError>(widget);
		}

		if (pinned)
		{
			var profileFolders = _folderCache.GetFoldersByProfileId(folder.ProfileId);
			var profile = _profileCache.GetById(folder.ProfileId);
			var conflicts = PinnedWidgetLayout.FindPinConflicts(widget,
				folder,
				targetScope,
				profileFolders,
				profile?.DefaultRows ?? GridDefaults.Rows,
				profile?.DefaultColumns ?? GridDefaults.Columns);
			if (conflicts.Count > 0)
			{
				var names = string.Join(", ", conflicts.Select(f => f.Name));
				return Result.Fail<WidgetEntity, WidgetError>(WidgetError.PositionOccupied,
					$"The widget does not fit at this position in: {names}");
			}
		}

		widget.IsPinned = pinned;
		widget.PinScope = resultingScope;
		_folderCache.UpdateWidget(folderId, widget);

		await _mediator.Publish(new WidgetUpdatedNotification(widget, DataChanged: false));

		return Result.Ok<WidgetEntity, WidgetError>(widget);
	}

	public async Task<Result<List<WidgetEntity>, WidgetError>> CreateMany(
		Guid folderId,
		IReadOnlyList<WidgetEntity> widgets,
		IReadOnlyList<Guid>? replaceIds = null,
		IReadOnlyList<Guid?>? sourceWidgetIds = null)
	{
		var folder = _folderCache.GetFolderById(folderId);
		if (folder is null)
		{
			return Result.Fail<List<WidgetEntity>, WidgetError>(WidgetError.FolderNotFound, "Folder not found");
		}

		if (widgets.Count == 0)
		{
			return Result.Fail<List<WidgetEntity>, WidgetError>(WidgetError.ValidationError, "No widgets to create");
		}

		if (sourceWidgetIds is not null && sourceWidgetIds.Count != widgets.Count)
		{
			return Result.Fail<List<WidgetEntity>, WidgetError>(WidgetError.ValidationError,
				"Source widget ids must match the widgets being created");
		}

		replaceIds ??= [];
		if (replaceIds.Distinct().Count() != replaceIds.Count)
		{
			return Result.Fail<List<WidgetEntity>, WidgetError>(WidgetError.ValidationError,
				"Duplicate widget ids in replace batch");
		}

		var widgetsById = folder.Widgets.ToDictionary(w => w.Id);
		var replaceTargets = new List<WidgetEntity>(replaceIds.Count);
		foreach (var replaceId in replaceIds)
		{
			if (!widgetsById.TryGetValue(replaceId, out var target))
			{
				return Result.Fail<List<WidgetEntity>, WidgetError>(WidgetError.NotFound, "Widget not found");
			}

			replaceTargets.Add(target);
		}

		var profileFolders = _folderCache.GetFoldersByProfileId(folder.ProfileId);
		var (effectiveColumns, effectiveRows) = ResolveGrid(folder, profileFolders);

		foreach (var widget in widgets)
		{
			var placement = new WidgetPlacement(widget.Id,
				widget.PositionX,
				widget.PositionY,
				widget.Width,
				widget.Height);
			if (WidgetLayoutValidation.OutOfBounds(placement, effectiveColumns, effectiveRows))
			{
				return Result.Fail<List<WidgetEntity>, WidgetError>(WidgetError.ValidationError,
					"Placement outside the folder grid");
			}
		}

		var sourceWidgetIdsByTargetId = new Dictionary<Guid, Guid>();
		for (var i = 0; i < widgets.Count; i++)
		{
			widgets[i].Id = Guid.NewGuid();
			widgets[i].IsPinned = false;
			widgets[i].PinScope = PinScope.Profile;

			if (sourceWidgetIds?[i] is { } sourceWidgetId)
			{
				sourceWidgetIdsByTargetId[widgets[i].Id] = sourceWidgetId;
			}
		}

		var addedPlacements = widgets
			.Select(w => new WidgetPlacement(w.Id, w.PositionX, w.PositionY, w.Width, w.Height))
			.ToList();

		var foreignPinned = PinnedWidgetLayout
			.PinnedWidgetsOutside(folder, profileFolders)
			.ToList();

		var removedIds = replaceIds.Count == 0 ? EmptyRemovedIds : replaceIds.ToHashSet();

		if (WidgetLayoutValidation.HasOverlap(folder, foreignPinned, [], addedPlacements, removedIds))
		{
			return Result.Fail<List<WidgetEntity>, WidgetError>(WidgetError.PositionOccupied,
				"Placements overlap the final layout");
		}

		foreach (var widget in widgets)
		{
			widget.FolderId = folderId;
			widget.CreatedAt = DateTime.UtcNow;
			widget.Data = await _widgetSecretCloner.CloneReferencedSecrets(widget.Data);
		}

		var created = widgets.ToList();

		if (replaceTargets.Count > 0)
		{
			var dataSnapshots = replaceTargets.Select(w => w.Data).ToList();

			// A same-folder group cut+paste passes the cut widgets as replaceIds, so they are exactly
			// the sources sourceWidgetIds points at. Snapshotting must happen before
			// Publish(WidgetsDeletedNotification) below: that notification reaches
			// ActionButtonStateVariableWidgetsDeletedHandler, which deletes each replaced widget's
			// variables synchronously, so snapshotting any later would already find them gone.
			var variableSnapshots = new List<(Guid TargetId, IReadOnlyList<WidgetVariableSnapshot> Variables)>();
			foreach (var (targetId, sourceWidgetId) in sourceWidgetIdsByTargetId)
			{
				variableSnapshots.Add((targetId, await _widgetVariableCloner.Snapshot(sourceWidgetId)));
			}

			// One persisted write for both halves of the swap - never observable as a delete followed
			// by a separate create (issue #213).
			_folderCache.ReplaceWidgets(folderId, replaceIds, created);

			foreach (var data in dataSnapshots)
			{
				await _widgetSecretScrubber.ScrubReferencedSecrets(data);
			}

			foreach (var (targetId, snapshot) in variableSnapshots)
			{
				await _widgetVariableCloner.Restore(targetId, snapshot);
			}

			await _mediator.Publish(new WidgetsDeletedNotification(folderId, replaceIds.ToList()));
			await _mediator.Publish(new WidgetsCreatedNotification(folderId, created));
		}
		else
		{
			_folderCache.AddWidgets(folderId, created);

			foreach (var (targetId, sourceWidgetId) in sourceWidgetIdsByTargetId)
			{
				await _widgetVariableCloner.Clone(sourceWidgetId, targetId);
			}

			await _mediator.Publish(new WidgetsCreatedNotification(folderId, created));
		}

		return Result.Ok<List<WidgetEntity>, WidgetError>(created);
	}

	public async Task<Result<WidgetError>> DeleteMany(Guid folderId, IReadOnlyList<Guid> widgetIds)
	{
		var folder = _folderCache.GetFolderById(folderId);
		if (folder is null)
		{
			return Result.Fail(WidgetError.FolderNotFound, "Folder not found");
		}

		if (widgetIds.Count == 0)
		{
			return Result.Fail(WidgetError.ValidationError, "No widgets to delete");
		}

		if (widgetIds.Distinct().Count() != widgetIds.Count)
		{
			return Result.Fail(WidgetError.ValidationError, "Duplicate widget ids in delete batch");
		}

		var widgetsById = folder.Widgets.ToDictionary(w => w.Id);
		var targets = new List<WidgetEntity>(widgetIds.Count);
		foreach (var widgetId in widgetIds)
		{
			if (!widgetsById.TryGetValue(widgetId, out var widget))
			{
				return Result.Fail(WidgetError.NotFound, "Widget not found");
			}

			targets.Add(widget);
		}

		var dataSnapshots = targets.Select(w => w.Data).ToList();

		_folderCache.RemoveWidgets(folderId, widgetIds.ToList());

		foreach (var data in dataSnapshots)
		{
			await _widgetSecretScrubber.ScrubReferencedSecrets(data);
		}

		await _mediator.Publish(new WidgetsDeletedNotification(folderId, widgetIds.ToList()));

		return Result.Ok<WidgetError>();
	}

	public async Task<Result<List<WidgetEntity>, WidgetError>> SetPinnedMany(
		Guid folderId,
		IReadOnlyList<Guid> widgetIds,
		bool pinned,
		PinScope? scope = null)
	{
		var folder = _folderCache.GetFolderById(folderId);
		if (folder is null)
		{
			return Result.Fail<List<WidgetEntity>, WidgetError>(WidgetError.FolderNotFound, "Folder not found");
		}

		if (widgetIds.Count == 0)
		{
			return Result.Fail<List<WidgetEntity>, WidgetError>(WidgetError.ValidationError, "No widgets to pin");
		}

		if (widgetIds.Distinct().Count() != widgetIds.Count)
		{
			return Result.Fail<List<WidgetEntity>, WidgetError>(WidgetError.ValidationError,
				"Duplicate widget ids in pin batch");
		}

		var widgetsById = folder.Widgets.ToDictionary(w => w.Id);
		var targets = new List<WidgetEntity>(widgetIds.Count);
		foreach (var widgetId in widgetIds)
		{
			if (!widgetsById.TryGetValue(widgetId, out var widget))
			{
				return Result.Fail<List<WidgetEntity>, WidgetError>(WidgetError.NotFound, "Widget not found");
			}

			targets.Add(widget);
		}

		if (pinned)
		{
			var profileFolders = _folderCache.GetFoldersByProfileId(folder.ProfileId);
			var profile = _profileCache.GetById(folder.ProfileId);

			var conflictingFolders = new Dictionary<Guid, FolderEntity>();
			foreach (var widget in targets)
			{
				var targetScope = scope ?? widget.PinScope;
				var conflicts = PinnedWidgetLayout.FindPinConflicts(widget,
					folder,
					targetScope,
					profileFolders,
					profile?.DefaultRows ?? GridDefaults.Rows,
					profile?.DefaultColumns ?? GridDefaults.Columns);
				foreach (var conflict in conflicts)
				{
					conflictingFolders[conflict.Id] = conflict;
				}
			}

			if (conflictingFolders.Count > 0)
			{
				var names = string.Join(", ", conflictingFolders.Values.Select(f => f.Name));
				return Result.Fail<List<WidgetEntity>, WidgetError>(WidgetError.PositionOccupied,
					$"The widgets do not fit at this position in: {names}");
			}
		}

		var changed = new List<WidgetEntity>();
		foreach (var widget in targets)
		{
			var targetScope = scope ?? widget.PinScope;
			var resultingScope = pinned ? targetScope : PinScope.Profile;
			if (widget.IsPinned == pinned && widget.PinScope == resultingScope)
			{
				continue;
			}

			widget.IsPinned = pinned;
			widget.PinScope = resultingScope;
			changed.Add(widget);
		}

		if (changed.Count == 0)
		{
			return Result.Ok<List<WidgetEntity>, WidgetError>(targets);
		}

		_folderCache.UpdateWidgets(folderId, changed);

		await _mediator.Publish(new WidgetsUpdatedNotification(folderId, changed));

		return Result.Ok<List<WidgetEntity>, WidgetError>(targets);
	}

	private (int Columns, int Rows) ResolveGrid(FolderEntity folder, IReadOnlyCollection<FolderEntity> profileFolders)
	{
		var profile = _profileCache.GetById(folder.ProfileId);
		return (
			GridInheritance.ResolveColumns(folder, profileFolders, profile?.DefaultColumns ?? GridDefaults.Columns),
			GridInheritance.ResolveRows(folder, profileFolders, profile?.DefaultRows ?? GridDefaults.Rows));
	}

	private static bool SameRect(WidgetEntity a, WidgetEntity b)
	{
		return a.PositionX == b.PositionX &&
			a.PositionY == b.PositionY &&
			a.Width == b.Width &&
			a.Height == b.Height;
	}
}
