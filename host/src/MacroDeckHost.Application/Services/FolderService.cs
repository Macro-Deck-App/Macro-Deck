using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Deck;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.FolderViews;
using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Profiles;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using Mediator;

namespace MacroDeckHost.Application.Services;

public class FolderService : IFolderService
{
	private readonly IFolderCache _folderCache;
	private readonly IProfileCache _profileCache;
	private readonly IDeviceRepository _deviceRepository;
	private readonly IMediator _mediator;
	private readonly IWidgetSecretCloner _widgetSecretCloner;
	private readonly IWidgetSecretScrubber _widgetSecretScrubber;
	private readonly IWidgetVariableCloner _widgetVariableCloner;
	private readonly IFolderViewRegistry _folderViews;

	public FolderService(
		IFolderCache folderCache,
		IProfileCache profileCache,
		IDeviceRepository deviceRepository,
		IMediator mediator,
		IWidgetSecretCloner widgetSecretCloner,
		IWidgetSecretScrubber widgetSecretScrubber,
		IWidgetVariableCloner widgetVariableCloner,
		IFolderViewRegistry folderViews)
	{
		_folderCache = folderCache;
		_profileCache = profileCache;
		_deviceRepository = deviceRepository;
		_mediator = mediator;
		_widgetSecretCloner = widgetSecretCloner;
		_widgetSecretScrubber = widgetSecretScrubber;
		_widgetVariableCloner = widgetVariableCloner;
		_folderViews = folderViews;
	}

	public async Task<Result<FolderEntity, FolderError>> Create(
		Guid profileId,
		string name,
		Guid? parentId,
		string? folderViewId = null,
		string? folderViewConfiguration = null)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return Result.Fail<FolderEntity, FolderError>(FolderError.ValidationError, "Name is required");
		}

		var view = FolderViewSelection.Validate(_folderViews, folderViewId, folderViewConfiguration);
		if (!view.Success)
		{
			return Result.Fail<FolderEntity, FolderError>(view.Error!.Value, view.ErrorMessage);
		}

		var profile = _profileCache.GetById(profileId);
		if (profile is null)
		{
			return Result.Fail<FolderEntity, FolderError>(FolderError.ValidationError, "Profile not found");
		}

		if (parentId.HasValue)
		{
			var parent = _folderCache.GetFolderById(parentId.Value);
			if (parent is null || parent.ProfileId != profileId)
			{
				return Result.Fail<FolderEntity, FolderError>(FolderError.InvalidParent, "Parent folder not found");
			}
		}

		var profileFolders = _folderCache.GetFoldersByProfileId(profileId);
		var siblings = profileFolders.Where(f => f.ParentId == parentId).ToList();
		var nextOrder = siblings.Count > 0 ? siblings.Max(f => f.Order) + 1 : 0;

		var folder = new FolderEntity
		{
			Id = Guid.NewGuid(),
			ProfileId = profileId,
			Name = name,
			ParentId = parentId,
			Order = nextOrder,
			// Inherited (issue #246), not copied, so a later profile default edit reaches it (issue #32).
			Rows = null,
			Columns = null,
			BackgroundColor = profile.DefaultBackgroundColor,
			IsDefault = parentId is null && !profileFolders.Any(existing => existing.ParentId is null),
			ViewId = view.Data.ViewId,
			ViewConfiguration = view.Data.Configuration,
			CreatedAt = DateTime.UtcNow
		};

		var effectiveRows = GridInheritance.ResolveRows(folder, profileFolders, profile.DefaultRows);
		var effectiveColumns = GridInheritance.ResolveColumns(folder, profileFolders, profile.DefaultColumns);
		PinnedWidgetLayout.GrowToFitPinned(folder, profileFolders, effectiveColumns, effectiveRows);

		await _folderCache.AddOrUpdate(folder);

		await _mediator.Publish(new FolderCreatedNotification(folder));

		return Result.Ok<FolderEntity, FolderError>(folder);
	}

	public async Task<Result<FolderEntity, FolderError>> Update(
		Guid id,
		string? name,
		Guid? parentId,
		int? order,
		int? rows,
		int? columns,
		string? backgroundColor,
		int? widgetSpacing,
		int? widgetBorderRadius,
		bool? isDefault,
		string? folderViewId = null,
		string? folderViewConfiguration = null)
	{
		var folder = _folderCache.GetFolderById(id);
		if (folder is null)
		{
			return Result.Fail<FolderEntity, FolderError>(FolderError.NotFound, "Folder not found");
		}

		var profile = _profileCache.GetById(folder.ProfileId);
		if (profile is null)
		{
			return Result.Fail<FolderEntity, FolderError>(FolderError.InternalError, "Profile not found");
		}

		var profileFolders = _folderCache.GetFoldersByProfileId(folder.ProfileId);
		var resolvedParentId = folder.ParentId;
		if (parentId is not null)
		{
			if (parentId == id)
			{
				return Result.Fail<FolderEntity, FolderError>(FolderError.InvalidParent,
					"Folder cannot be its own parent");
			}

			if (parentId != Guid.Empty)
			{
				var parent = _folderCache.GetFolderById(parentId.Value);
				if (parent is null || parent.ProfileId != folder.ProfileId)
				{
					return Result.Fail<FolderEntity, FolderError>(FolderError.InvalidParent, "Parent folder not found");
				}

				if (IsDescendantOf(parent.Id, folder.Id))
				{
					return Result.Fail<FolderEntity, FolderError>(FolderError.InvalidParent,
						"Folder cannot be moved into one of its descendants");
				}
			}

			resolvedParentId = parentId == Guid.Empty ? null : parentId;
		}

		if (name is not null && string.IsNullOrWhiteSpace(name))
		{
			return Result.Fail<FolderEntity, FolderError>(FolderError.ValidationError, "Name cannot be empty");
		}

		if (isDefault is true && resolvedParentId is not null)
		{
			return Result.Fail<FolderEntity, FolderError>(FolderError.ValidationError,
				"Only a root folder can be the start folder");
		}

		if (isDefault is false && folder.IsDefault)
		{
			return Result.Fail<FolderEntity, FolderError>(FolderError.ValidationError,
				"The profile's start folder cannot be disabled");
		}

		// Validate every requested mutation before changing the cached aggregate. A rejected compound
		// update must not leave an in-memory parent or start marker that was never persisted.
		if (rows is < 1 and not -1)
		{
			return Result.Fail<FolderEntity, FolderError>(FolderError.ValidationError, "Rows must be at least 1");
		}

		if (columns is < 1 and not -1)
		{
			return Result.Fail<FolderEntity, FolderError>(FolderError.ValidationError,
				"Columns must be at least 1");
		}

		if (rows.HasValue || columns.HasValue || parentId is not null)
		{
			var candidateRows = rows.HasValue ? (rows.Value == -1 ? null : rows.Value) : folder.Rows;
			var candidateColumns = columns.HasValue ? (columns.Value == -1 ? null : columns.Value) : folder.Columns;
			var conflict = FindNewGridConflicts(profile,
				profileFolders,
				folder.Id,
				candidateRows,
				candidateColumns,
				resolvedParentId);
			if (conflict is not null)
			{
				return Result.Fail<FolderEntity, FolderError>(FolderError.GridTooSmall, conflict);
			}

			var claimingDevices = await _deviceRepository.GetByStartupProfileId(folder.ProfileId.ToString());
			var lockViolation = FindGridLockViolation(profile,
				profileFolders,
				claimingDevices,
				folder.Id,
				candidateRows,
				candidateColumns,
				resolvedParentId,
				rows.HasValue,
				columns.HasValue);
			if (lockViolation is not null)
			{
				return Result.Fail<FolderEntity, FolderError>(FolderError.GridLockedByDevice, lockViolation);
			}
		}

		// Only a reparent can change which subtree pins reach this folder (or its descendants) - a bare
		// grid edit cannot, so this is checked independently of the grid-fit conflict above.
		if (parentId is not null)
		{
			var reachConflict = FindNewPinReachConflicts(profileFolders, folder.Id, resolvedParentId);
			if (reachConflict is not null)
			{
				return Result.Fail<FolderEntity, FolderError>(FolderError.PinnedWidgetConflict, reachConflict);
			}
		}

		if (widgetSpacing.HasValue &&
			widgetSpacing.Value != -1 &&
			(widgetSpacing.Value < 0 || widgetSpacing.Value > 40))
		{
			return Result.Fail<FolderEntity, FolderError>(FolderError.ValidationError,
				"Widget spacing must be between 0 and 40");
		}

		if (widgetBorderRadius.HasValue &&
			widgetBorderRadius.Value != -1 &&
			(widgetBorderRadius.Value < 0 || widgetBorderRadius.Value > 60))
		{
			return Result.Fail<FolderEntity, FolderError>(FolderError.ValidationError,
				"Widget border radius must be between 0 and 60");
		}

		// Switching the view drops the previous view's configuration unless this call supplies a new one:
		// a configuration belongs to the view that wrote it and means nothing to the next one.
		var viewChanged = folderViewId is not null;
		var viewRequested = viewChanged || folderViewConfiguration is not null;

		// Only validated when this call actually touches the view. A folder whose provider has been
		// uninstalled still holds an id that resolves to nothing, and renaming or re-parenting such a
		// folder must keep working - validating the untouched current view would make an unrelated edit
		// fail, on exactly the folders the placeholder exists to keep usable.
		FolderViewSelection selection = default;
		if (viewRequested)
		{
			var candidateViewId = viewChanged ? folderViewId : folder.ViewId;
			var candidateConfiguration =
				folderViewConfiguration ?? (viewChanged ? null : folder.ViewConfiguration);

			var view = FolderViewSelection.Validate(_folderViews, candidateViewId, candidateConfiguration);
			if (!view.Success)
			{
				return Result.Fail<FolderEntity, FolderError>(view.Error!.Value, view.ErrorMessage);
			}

			selection = view.Data;
		}

		FolderEntity? replacementStart = null;
		if (folder.IsDefault && resolvedParentId is not null)
		{
			var remainingRoots = profileFolders
				.Where(existing => existing.Id != folder.Id && existing.ParentId is null)
				.ToList();
			if (remainingRoots.Count == 0)
			{
				return Result.Fail<FolderEntity, FolderError>(FolderError.InvalidParent,
					"The only root start folder cannot be moved inside another folder");
			}

			replacementStart = StartFolderInvariant.SelectOldestRoot(remainingRoots);
		}

		if (name is not null)
		{
			folder.Name = name;
		}

		var hasPlacementChange = parentId is not null || order.HasValue;
		List<FolderEntity> placementChanges = [];
		if (hasPlacementChange)
		{
			var targetParentId = parentId is not null ? resolvedParentId : folder.ParentId;
			placementChanges = ApplyPlacement(folder, profileFolders, targetParentId, order ?? int.MaxValue);
		}

		if (rows.HasValue)
		{
			folder.Rows = rows.Value == -1 ? null : rows.Value;
		}

		if (columns.HasValue)
		{
			folder.Columns = columns.Value == -1 ? null : columns.Value;
		}

		if (backgroundColor is not null)
		{
			folder.BackgroundColor = string.IsNullOrEmpty(backgroundColor) ? null : backgroundColor;
		}

		if (viewRequested)
		{
			folder.ViewId = selection.ViewId;
			folder.ViewConfiguration = selection.Configuration;
		}

		if (widgetSpacing.HasValue)
		{
			folder.WidgetSpacing = widgetSpacing.Value == -1 ? null : widgetSpacing.Value;
		}

		var radiusBefore = folder.WidgetBorderRadius;
		if (widgetBorderRadius.HasValue)
		{
			folder.WidgetBorderRadius = widgetBorderRadius.Value == -1 ? null : widgetBorderRadius.Value;
		}

		var cornerRadiusChanged = folder.WidgetBorderRadius != radiusBefore;


		var changedStartFolders = isDefault is true
			? StartFolderInvariant.MarkAsStart(profileFolders, folder)
			: replacementStart is not null
				? StartFolderInvariant.MarkAsStart(profileFolders, replacementStart)
				: [];

		if (hasPlacementChange)
		{
			await _folderCache.AddOrUpdateRange(placementChanges);
		}
		else
		{
			await _folderCache.AddOrUpdate(folder);
		}

		foreach (var changed in changedStartFolders.Where(changed => changed.Id != folder.Id))
		{
			await _mediator.Publish(new FolderUpdatedNotification(changed));
		}

		await _mediator.Publish(new FolderUpdatedNotification(folder, cornerRadiusChanged));

		if (hasPlacementChange && placementChanges.Count > 1)
		{
			await _mediator.Publish(new FoldersReorderedNotification(folder.ProfileId, placementChanges));
		}

		return Result.Ok<FolderEntity, FolderError>(folder);
	}

	public async Task<Result<FolderError>> Delete(Guid id)
	{
		var folder = _folderCache.GetFolderById(id);
		if (folder is null)
		{
			return Result.Fail(FolderError.NotFound, "Folder not found");
		}

		var profileFolders = _folderCache.GetFoldersByProfileId(folder.ProfileId);
		var subtree = FolderSubtree.Collect(profileFolders, folder);
		var subtreeIds = subtree.Select(f => f.Id).ToHashSet();

		if (profileFolders.Count <= subtreeIds.Count)
		{
			return Result.Fail(FolderError.CannotDeleteLastFolder, "Cannot delete the last folder");
		}

		if (subtree.Any(f => f.IsDefault) &&
			!profileFolders.Any(f => !subtreeIds.Contains(f.Id) && f.ParentId is null))
		{
			return Result.Fail(FolderError.CannotDeleteLastFolder, "Cannot delete the only root start folder");
		}

		var removal = await _folderCache.RemoveSubtree(id);
		if (!removal.Persisted)
		{
			return removal.Failure switch
			{
				FolderSubtreeRemovalFailure.RootNotFound => Result.Fail(FolderError.NotFound, "Folder not found"),
				FolderSubtreeRemovalFailure.LastFolder => Result.Fail(FolderError.CannotDeleteLastFolder,
					"Cannot delete the last folder"),
				FolderSubtreeRemovalFailure.OnlyRootStartFolder => Result.Fail(FolderError.CannotDeleteLastFolder,
					"Cannot delete the only root start folder"),
				_ => Result.Fail(FolderError.InternalError, "Failed to persist the folder deletion")
			};
		}

		var removedWidgets = removal.RemovedFolders.SelectMany(removedFolder => removedFolder.Widgets).ToList();

		foreach (var widget in removedWidgets)
		{
			await _widgetSecretScrubber.ScrubReferencedSecrets(widget.Data);
		}

		foreach (var changed in removal.ChangedStartFolders)
		{
			await _mediator.Publish(new FolderUpdatedNotification(changed));
		}

		foreach (var widget in removedWidgets)
		{
			await _mediator.Publish(new WidgetDeletedNotification(widget.Id, widget.FolderId));
		}

		foreach (var removedFolder in removal.RemovedFolders.Reverse())
		{
			await _mediator.Publish(new FolderDeletedNotification(removedFolder.Id));
		}

		return Result.Ok<FolderError>();
	}

	public async Task<Result<FolderEntity, FolderError>> Duplicate(Guid id)
	{
		var originalFolder = _folderCache.GetFolderById(id);
		if (originalFolder is null)
		{
			return Result.Fail<FolderEntity, FolderError>(FolderError.NotFound, "Folder not found");
		}

		var siblings = _folderCache.GetFoldersByParentId(originalFolder.ParentId);
		var nextOrder = siblings.Count > 0 ? siblings.Max(f => f.Order) + 1 : 0;

		var duplicateFolder = new FolderEntity
		{
			Id = Guid.NewGuid(),
			ProfileId = originalFolder.ProfileId,
			Name = $"{originalFolder.Name} (Copy)",
			ParentId = originalFolder.ParentId,
			Order = nextOrder,
			Rows = originalFolder.Rows,
			Columns = originalFolder.Columns,
			BackgroundColor = originalFolder.BackgroundColor,
			WidgetSpacing = originalFolder.WidgetSpacing,
			WidgetBorderRadius = originalFolder.WidgetBorderRadius,
			// Copied verbatim, including an id nothing currently provides: a copy of a folder whose
			// integration is stopped must come back with the original when it starts again.
			ViewId = originalFolder.ViewId,
			ViewConfiguration = originalFolder.ViewConfiguration,
			CreatedAt = DateTime.UtcNow
		};

		var profileFolders = _folderCache.GetFoldersByProfileId(originalFolder.ProfileId);
		var profile = _profileCache.GetById(originalFolder.ProfileId);
		var effectiveRows =
			GridInheritance.ResolveRows(duplicateFolder, profileFolders, profile?.DefaultRows ?? GridDefaults.Rows);
		var effectiveColumns = GridInheritance.ResolveColumns(duplicateFolder,
			profileFolders,
			profile?.DefaultColumns ?? GridDefaults.Columns);
		PinnedWidgetLayout.GrowToFitPinned(duplicateFolder, profileFolders, effectiveColumns, effectiveRows);

		var duplicatedWidgets = new List<WidgetEntity>();
		var sourceWidgetIdsByDuplicateId = new Dictionary<Guid, Guid>();
		foreach (var widget in originalFolder.Widgets.Where(w => !(w.IsPinned && w.PinScope == PinScope.Profile)))
		{
			var duplicateWidget = new WidgetEntity
			{
				Id = Guid.NewGuid(),
				FolderId = duplicateFolder.Id,
				Type = widget.Type,
				PositionX = widget.PositionX,
				PositionY = widget.PositionY,
				Width = widget.Width,
				Height = widget.Height,
				Data = await _widgetSecretCloner.CloneReferencedSecrets(widget.Data),
				IsPinned = widget.IsPinned,
				PinScope = widget.PinScope,
				CreatedAt = DateTime.UtcNow
			};
			duplicatedWidgets.Add(duplicateWidget);
			sourceWidgetIdsByDuplicateId[duplicateWidget.Id] = widget.Id;
		}

		duplicateFolder.Widgets = duplicatedWidgets;

		await _folderCache.AddOrUpdate(duplicateFolder);

		foreach (var (duplicateId, sourceId) in sourceWidgetIdsByDuplicateId)
		{
			await _widgetVariableCloner.Clone(sourceId, duplicateId);
		}

		await _mediator.Publish(new FolderCreatedNotification(duplicateFolder));
		foreach (var widget in duplicateFolder.Widgets)
		{
			await _mediator.Publish(new WidgetCreatedNotification(widget));
		}

		return Result.Ok<FolderEntity, FolderError>(duplicateFolder);
	}

	public async Task<Result<IReadOnlyList<FolderEntity>, FolderError>> Move(Guid id,
		Guid targetId,
		FolderMovePosition position)
	{
		var folder = _folderCache.GetFolderById(id);
		if (folder is null)
		{
			return Result.Fail<IReadOnlyList<FolderEntity>, FolderError>(FolderError.NotFound, "Folder not found");
		}

		var target = _folderCache.GetFolderById(targetId);
		if (target is null)
		{
			return Result.Fail<IReadOnlyList<FolderEntity>, FolderError>(FolderError.NotFound,
				"Target folder not found");
		}

		if (targetId == id)
		{
			return Result.Fail<IReadOnlyList<FolderEntity>, FolderError>(FolderError.InvalidParent,
				"Folder cannot be moved relative to itself");
		}

		if (target.ProfileId != folder.ProfileId)
		{
			return Result.Fail<IReadOnlyList<FolderEntity>, FolderError>(FolderError.InvalidParent,
				"Target folder belongs to a different profile");
		}

		var newParentId = position == FolderMovePosition.Inside ? target.Id : target.ParentId;

		if (newParentId is { } nonNullParentId && IsDescendantOf(nonNullParentId, folder.Id))
		{
			return Result.Fail<IReadOnlyList<FolderEntity>, FolderError>(FolderError.InvalidParent,
				"Folder cannot be moved into one of its descendants");
		}

		var profileFolders = _folderCache.GetFoldersByProfileId(folder.ProfileId);

		var profile = _profileCache.GetById(folder.ProfileId);
		if (profile is not null && newParentId != folder.ParentId)
		{
			var conflict = FindNewGridConflicts(profile,
				profileFolders,
				folder.Id,
				folder.Rows,
				folder.Columns,
				newParentId);
			if (conflict is not null)
			{
				return Result.Fail<IReadOnlyList<FolderEntity>, FolderError>(FolderError.GridTooSmall, conflict);
			}

			var reachConflict = FindNewPinReachConflicts(profileFolders, folder.Id, newParentId);
			if (reachConflict is not null)
			{
				return Result.Fail<IReadOnlyList<FolderEntity>, FolderError>(FolderError.PinnedWidgetConflict,
					reachConflict);
			}
		}

		FolderEntity? replacementStart = null;
		if (folder.IsDefault && newParentId is not null)
		{
			var remainingRoots = profileFolders
				.Where(existing => existing.Id != folder.Id && existing.ParentId is null)
				.ToList();
			if (remainingRoots.Count == 0)
			{
				return Result.Fail<IReadOnlyList<FolderEntity>, FolderError>(FolderError.InvalidParent,
					"The only root start folder cannot be moved inside another folder");
			}

			replacementStart = StartFolderInvariant.SelectOldestRoot(remainingRoots);
		}

		var index = ResolvePlacementIndex(profileFolders, folder.Id, newParentId, target, position);
		var placementChanges = ApplyPlacement(folder, profileFolders, newParentId, index);

		var changedStartFolders = replacementStart is not null
			? StartFolderInvariant.MarkAsStart(profileFolders, replacementStart)
			: [];

		var batch = placementChanges.Concat(changedStartFolders).DistinctBy(changed => changed.Id).ToList();

		await _folderCache.AddOrUpdateRange(batch);

		await _mediator.Publish(new FoldersReorderedNotification(folder.ProfileId, batch));

		return Result.Ok<IReadOnlyList<FolderEntity>, FolderError>(batch);
	}

	public async Task<Result<FolderFocusRule, FolderError>> SetFocusRule(Guid folderId, FolderFocusRule rule)
	{
		var folder = _folderCache.GetFolderById(folderId);
		if (folder is null)
		{
			return Result.Fail<FolderFocusRule, FolderError>(FolderError.NotFound, "Folder not found");
		}

		var normalizedIdentity = ApplicationIdentityMatcher.Normalize(rule.IdentityKind, rule.ApplicationIdentity);
		if (normalizedIdentity.Length == 0)
		{
			return Result.Fail<FolderFocusRule, FolderError>(FolderError.ValidationError,
				"Application identity is required");
		}

		var ruleId = rule.Id == Guid.Empty ? Guid.NewGuid() : rule.Id;
		var normalizedRule = new FolderFocusRule
		{
			Id = ruleId,
			Enabled = rule.Enabled,
			ApplicationIdentity = normalizedIdentity,
			IdentityKind = rule.IdentityKind,
			DeviceId = rule.DeviceId,
			ReturnOnFocusLoss = rule.ReturnOnFocusLoss
		};

		if (normalizedRule.Enabled)
		{
			var conflict = FindConflictingRule(normalizedRule, ruleId);
			if (conflict is not null)
			{
				return Result.Fail<FolderFocusRule, FolderError>(FolderError.DuplicateFocusRule,
					$"'{conflict.Value.Folder.Name}' already has a focus rule for this application on this device");
			}
		}

		folder.FocusRules = folder.FocusRules.Where(existing => existing.Id != ruleId).Append(normalizedRule).ToList();

		await _folderCache.AddOrUpdate(folder);

		await _mediator.Publish(new FolderFocusRuleChangedNotification(folderId));

		return Result.Ok<FolderFocusRule, FolderError>(normalizedRule);
	}

	public async Task<Result<FolderError>> DeleteFocusRule(Guid folderId, Guid ruleId)
	{
		var folder = _folderCache.GetFolderById(folderId);
		if (folder is null)
		{
			return Result.Fail(FolderError.NotFound, "Folder not found");
		}

		var rule = folder.FocusRules.FirstOrDefault(r => r.Id == ruleId);
		if (rule is null)
		{
			return Result.Fail(FolderError.NotFound, "Focus rule not found");
		}

		folder.FocusRules = folder.FocusRules.Where(existing => existing.Id != ruleId).ToList();

		await _folderCache.AddOrUpdate(folder);

		await _mediator.Publish(new FolderFocusRuleRemovedNotification(folderId, ruleId));

		return Result.Ok<FolderError>();
	}

	private (FolderEntity Folder, FolderFocusRule Rule)? FindConflictingRule(FolderFocusRule candidate,
		Guid excludeRuleId)
	{
		foreach (var folder in _folderCache.GetAllFolders())
		{
			foreach (var existing in folder.FocusRules)
			{
				if (existing.Id != excludeRuleId &&
					existing.Enabled &&
					ApplicationIdentityMatcher.SameTarget(candidate, existing))
				{
					return (folder, existing);
				}
			}
		}

		return null;
	}

	private static string? FindNewGridConflicts(
		ProfileEntity profile,
		IReadOnlyList<FolderEntity> profileFolders,
		Guid folderId,
		int? candidateRows,
		int? candidateColumns,
		Guid? candidateParentId)
	{
		var currentConflicts =
			GridInheritance.FindGridConflicts(profileFolders, profile.DefaultRows, profile.DefaultColumns);
		var candidateFolders = GridInheritance.WithCandidate(profileFolders,
			folderId,
			candidateRows,
			candidateColumns,
			candidateParentId);
		var candidateConflicts =
			GridInheritance.FindGridConflicts(candidateFolders, profile.DefaultRows, profile.DefaultColumns);

		var newConflicts = GridInheritance.NewConflicts(currentConflicts, candidateConflicts);
		return newConflicts.Count > 0 ? GridInheritance.DescribeConflicts(newConflicts) : null;
	}

	private static string? FindGridLockViolation(
		ProfileEntity profile,
		IReadOnlyList<FolderEntity> profileFolders,
		IReadOnlyList<DeviceEntity> claimingDevices,
		Guid folderId,
		int? candidateRows,
		int? candidateColumns,
		Guid? candidateParentId,
		bool rowsProvided,
		bool columnsProvided)
	{
		var constraint = DeviceLayoutConstraintResolver.Resolve(claimingDevices);
		if (constraint is null)
		{
			return null;
		}

		var candidateFolders = GridInheritance.WithCandidate(profileFolders,
			folderId,
			candidateRows,
			candidateColumns,
			candidateParentId);
		var candidateFolder = candidateFolders.First(f => f.Id == folderId);

		var effectiveRows = rowsProvided
			? GridInheritance.ResolveRows(candidateFolder, candidateFolders, profile.DefaultRows)
			: (int?)null;
		var effectiveColumns = columnsProvided
			? GridInheritance.ResolveColumns(candidateFolder, candidateFolders, profile.DefaultColumns)
			: (int?)null;

		if (constraint.AllowsEdit(effectiveRows, effectiveColumns))
		{
			return null;
		}

		// See ProfileService.Update's identical GridLockedByDevice branch for why this stays a plain
		// sentence rather than going through AppStrings.
		return $"{constraint.DeviceName} locks this grid to {constraint.Columns} x {constraint.Rows}";
	}

	private static string? FindNewPinReachConflicts(
		IReadOnlyList<FolderEntity> profileFolders,
		Guid folderId,
		Guid? candidateParentId)
	{
		var folder = profileFolders.First(f => f.Id == folderId);
		var candidateFolders = GridInheritance.WithCandidate(profileFolders,
			folderId,
			folder.Rows,
			folder.Columns,
			candidateParentId);
		var conflicts = PinnedWidgetLayout.FindNewlyReachedOverlaps(profileFolders, candidateFolders);
		return conflicts.Count > 0 ? PinnedWidgetLayout.DescribeReachConflicts(conflicts) : null;
	}

	private static int ResolvePlacementIndex(IReadOnlyList<FolderEntity> profileFolders,
		Guid movingFolderId,
		Guid? newParentId,
		FolderEntity target,
		FolderMovePosition position)
	{
		if (position == FolderMovePosition.Inside)
		{
			return int.MaxValue;
		}

		var destinationSiblings = profileFolders
			.Where(f => f.Id != movingFolderId && f.ParentId == newParentId)
			.OrderBy(f => f.Order)
			.ThenBy(f => f.Id)
			.ToList();

		var targetIndex = destinationSiblings.FindIndex(f => f.Id == target.Id);
		if (targetIndex < 0)
		{
			return int.MaxValue;
		}

		return position == FolderMovePosition.Before ? targetIndex : targetIndex + 1;
	}

	private static List<FolderEntity> ApplyPlacement(FolderEntity folder,
		IReadOnlyList<FolderEntity> profileFolders,
		Guid? newParentId,
		int index)
	{
		var oldParentId = folder.ParentId;

		var destinationSiblings = profileFolders
			.Where(f => f.Id != folder.Id && f.ParentId == newParentId)
			.OrderBy(f => f.Order)
			.ThenBy(f => f.Id)
			.ToList();

		var sourceSiblings = oldParentId == newParentId
			? destinationSiblings
			: profileFolders
				.Where(f => f.Id != folder.Id && f.ParentId == oldParentId)
				.OrderBy(f => f.Order)
				.ThenBy(f => f.Id)
				.ToList();

		folder.ParentId = newParentId;

		var changed = new List<FolderEntity> { folder };

		destinationSiblings.Insert(Math.Clamp(index, 0, destinationSiblings.Count), folder);
		Renumber(destinationSiblings, changed);

		if (oldParentId != newParentId)
		{
			Renumber(sourceSiblings, changed);
		}

		return changed;
	}

	private static void Renumber(List<FolderEntity> siblings, List<FolderEntity> changed)
	{
		for (var i = 0; i < siblings.Count; i++)
		{
			var sibling = siblings[i];
			if (sibling.Order == i)
			{
				continue;
			}

			sibling.Order = i;
			if (!changed.Contains(sibling))
			{
				changed.Add(sibling);
			}
		}
	}

	private bool IsDescendantOf(Guid possibleDescendantId, Guid ancestorId)
	{
		var visited = new HashSet<Guid>();
		var currentId = possibleDescendantId;
		while (visited.Add(currentId))
		{
			if (currentId == ancestorId)
			{
				return true;
			}

			var current = _folderCache.GetFolderById(currentId);
			if (current?.ParentId is not { } parentId)
			{
				return false;
			}

			currentId = parentId;
		}

		// A pre-existing malformed cycle must not gain another edge through this update.
		return true;
	}
}
