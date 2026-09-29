using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Portable;
using MacroDeckHost.Application.Profiles;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using Mediator;

namespace MacroDeckHost.Application.Services;

public class ProfileService : IProfileService
{
	private readonly IProfileCache _profileCache;
	private readonly IFolderCache _folderCache;
	private readonly IDeviceRepository _deviceRepository;
	private readonly IMediator _mediator;
	private readonly IWidgetSecretCloner _widgetSecretCloner;
	private readonly IWidgetVariableCloner _widgetVariableCloner;

	public ProfileService(
		IProfileCache profileCache,
		IFolderCache folderCache,
		IDeviceRepository deviceRepository,
		IMediator mediator,
		IWidgetSecretCloner widgetSecretCloner,
		IWidgetVariableCloner widgetVariableCloner)
	{
		_profileCache = profileCache;
		_folderCache = folderCache;
		_deviceRepository = deviceRepository;
		_mediator = mediator;
		_widgetSecretCloner = widgetSecretCloner;
		_widgetVariableCloner = widgetVariableCloner;
	}

	public async Task<Result<ProfileEntity, ProfileError>> Create(
		string name,
		ProfileLayoutType? layoutType = null,
		int? defaultRows = null,
		int? defaultColumns = null,
		string? defaultBackgroundColor = null,
		int? defaultWidgetSpacing = null,
		int? defaultWidgetBorderRadius = null,
		string? defaultEmptyCellStyle = null)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return Result.Fail<ProfileEntity, ProfileError>(ProfileError.ValidationError, "Name is required");
		}

		if (defaultRows is < 1)
		{
			return Result.Fail<ProfileEntity, ProfileError>(ProfileError.ValidationError,
				"Default rows must be at least 1");
		}

		if (defaultColumns is < 1)
		{
			return Result.Fail<ProfileEntity, ProfileError>(ProfileError.ValidationError,
				"Default columns must be at least 1");
		}

		if (defaultWidgetSpacing is < 0 or > 40)
		{
			return Result.Fail<ProfileEntity, ProfileError>(ProfileError.ValidationError,
				"Default widget spacing must be between 0 and 40");
		}

		if (defaultWidgetBorderRadius is < 0 or > 60)
		{
			return Result.Fail<ProfileEntity, ProfileError>(ProfileError.ValidationError,
				"Default widget border radius must be between 0 and 60");
		}

		EmptyCellStyle? emptyCellStyle = null;
		if (defaultEmptyCellStyle is not null &&
			!EmptyCellStyleText.TryParseUpdate(defaultEmptyCellStyle, out emptyCellStyle))
		{
			return Result.Fail<ProfileEntity, ProfileError>(ProfileError.ValidationError,
				"Default empty cell style must be visible or transparent");
		}

		var existing = _profileCache.GetAll();
		var nextOrder = existing.Count > 0 ? existing.Max(p => p.Order) + 1 : 0;

		var profile = new ProfileEntity
		{
			Id = Guid.NewGuid(),
			Name = name,
			Order = nextOrder,
			LayoutType = layoutType ?? ProfileLayoutType.Grid,
			DefaultRows = defaultRows ?? GridDefaults.Rows,
			DefaultColumns = defaultColumns ?? GridDefaults.Columns,
			DefaultBackgroundColor = string.IsNullOrEmpty(defaultBackgroundColor) ? null : defaultBackgroundColor,
			DefaultWidgetSpacing = defaultWidgetSpacing,
			DefaultWidgetBorderRadius = defaultWidgetBorderRadius,
			DefaultEmptyCellStyle = emptyCellStyle,
			CreatedAt = DateTime.UtcNow
		};

		// A profile is persisted as one aggregate, so its first on-disk representation already has the
		// required entry point instead of briefly existing as an empty deck. Inherited (issue #246), not
		// copied, so a later profile default edit reaches it (issue #32).
		var startFolder = new FolderEntity
		{
			Id = Guid.NewGuid(),
			ProfileId = profile.Id,
			Name = "Home",
			Order = 0,
			Rows = null,
			Columns = null,
			BackgroundColor = profile.DefaultBackgroundColor,
			IsDefault = true,
			CreatedAt = DateTime.UtcNow
		};
		await _profileCache.AddOrUpdateAggregate(profile, [startFolder]);

		await _mediator.Publish(new ProfileCreatedNotification(profile));
		await _mediator.Publish(new FolderCreatedNotification(startFolder));

		return Result.Ok<ProfileEntity, ProfileError>(profile);
	}

	public async Task<Result<ProfileEntity, ProfileError>> Update(
		Guid id,
		string? name,
		int? order,
		int? defaultRows,
		int? defaultColumns,
		string? defaultBackgroundColor,
		int? defaultWidgetSpacing,
		int? defaultWidgetBorderRadius,
		string? defaultEmptyCellStyle = null)
	{
		var profile = _profileCache.GetById(id);
		if (profile is null)
		{
			return Result.Fail<ProfileEntity, ProfileError>(ProfileError.NotFound, "Profile not found");
		}

		if (name is not null && string.IsNullOrWhiteSpace(name))
		{
			return Result.Fail<ProfileEntity, ProfileError>(ProfileError.ValidationError, "Name cannot be empty");
		}

		if (defaultRows is < 1)
		{
			return Result.Fail<ProfileEntity, ProfileError>(ProfileError.ValidationError,
				"Default rows must be at least 1");
		}

		if (defaultColumns is < 1)
		{
			return Result.Fail<ProfileEntity, ProfileError>(ProfileError.ValidationError,
				"Default columns must be at least 1");
		}

		if (defaultWidgetSpacing.HasValue &&
			defaultWidgetSpacing.Value != -1 &&
			(defaultWidgetSpacing.Value < 0 || defaultWidgetSpacing.Value > 40))
		{
			return Result.Fail<ProfileEntity, ProfileError>(ProfileError.ValidationError,
				"Default widget spacing must be between 0 and 40");
		}

		if (defaultWidgetBorderRadius.HasValue &&
			defaultWidgetBorderRadius.Value != -1 &&
			(defaultWidgetBorderRadius.Value < 0 || defaultWidgetBorderRadius.Value > 60))
		{
			return Result.Fail<ProfileEntity, ProfileError>(ProfileError.ValidationError,
				"Default widget border radius must be between 0 and 60");
		}

		EmptyCellStyle? emptyCellStyle = null;
		if (defaultEmptyCellStyle is not null &&
			!EmptyCellStyleText.TryParseUpdate(defaultEmptyCellStyle, out emptyCellStyle))
		{
			return Result.Fail<ProfileEntity, ProfileError>(ProfileError.ValidationError,
				"Default empty cell style must be visible or transparent");
		}

		if (defaultRows.HasValue || defaultColumns.HasValue)
		{
			var candidateRows = defaultRows ?? profile.DefaultRows;
			var candidateColumns = defaultColumns ?? profile.DefaultColumns;
			var profileFolders = _folderCache.GetFoldersByProfileId(id);

			var currentConflicts =
				GridInheritance.FindGridConflicts(profileFolders, profile.DefaultRows, profile.DefaultColumns);
			var candidateConflicts =
				GridInheritance.FindGridConflicts(profileFolders, candidateRows, candidateColumns);

			var newConflicts = GridInheritance.NewConflicts(currentConflicts, candidateConflicts);
			if (newConflicts.Count > 0)
			{
				return Result.Fail<ProfileEntity, ProfileError>(ProfileError.GridTooSmall,
					GridInheritance.DescribeConflicts(newConflicts));
			}

			var claimingDevices = await _deviceRepository.GetByStartupProfileId(id.ToString());
			var constraint = DeviceLayoutConstraintResolver.Resolve(claimingDevices);
			if (constraint is not null && !constraint.AllowsEdit(defaultRows, defaultColumns))
			{
				// Kept a plain, unresolved sentence rather than routed through AppStrings, exactly like
				// the GridTooSmall reason above: this service has no scope to resolve a LocalizedText
				// with, and Result.ErrorMessage is a diagnostic string, not a client-facing one - the
				// resx key added for this rejection (Errors.Profiles.GridLockedByDevice) is what a
				// caller with a resolver reaches for instead.
				return Result.Fail<ProfileEntity, ProfileError>(ProfileError.GridLockedByDevice,
					$"{constraint.DeviceName} locks this grid to {constraint.Columns} x {constraint.Rows}");
			}
		}

		if (name is not null)
		{
			profile.Name = name;
		}

		if (order.HasValue)
		{
			profile.Order = order.Value;
		}

		if (defaultRows.HasValue)
		{
			profile.DefaultRows = defaultRows.Value;
		}

		if (defaultColumns.HasValue)
		{
			profile.DefaultColumns = defaultColumns.Value;
		}

		if (defaultBackgroundColor is not null)
		{
			profile.DefaultBackgroundColor =
				string.IsNullOrEmpty(defaultBackgroundColor) ? null : defaultBackgroundColor;
		}

		if (defaultWidgetSpacing.HasValue)
		{
			profile.DefaultWidgetSpacing = defaultWidgetSpacing.Value == -1 ? null : defaultWidgetSpacing.Value;
		}

		if (defaultWidgetBorderRadius.HasValue)
		{
			profile.DefaultWidgetBorderRadius =
				defaultWidgetBorderRadius.Value == -1 ? null : defaultWidgetBorderRadius.Value;
		}

		if (defaultEmptyCellStyle is not null)
		{
			profile.DefaultEmptyCellStyle = emptyCellStyle;
		}

		await _profileCache.AddOrUpdate(profile);

		await _mediator.Publish(new ProfileUpdatedNotification(profile));

		return Result.Ok<ProfileEntity, ProfileError>(profile);
	}

	public async Task<Result<ProfileError>> Delete(Guid id)
	{
		var profile = _profileCache.GetById(id);
		if (profile is null)
		{
			return Result.Fail(ProfileError.NotFound, "Profile not found");
		}

		if (_profileCache.GetAll().Count <= 1)
		{
			return Result.Fail(ProfileError.CannotDeleteLastProfile, "Cannot delete the last profile");
		}

		await _profileCache.Remove(id);

		await _mediator.Publish(new ProfileDeletedNotification(id));

		return Result.Ok<ProfileError>();
	}

	public async Task<Result<ProfileEntity, ProfileError>> Duplicate(Guid id, string? name = null)
	{
		var source = _profileCache.GetById(id);
		if (source is null)
		{
			return Result.Fail<ProfileEntity, ProfileError>(ProfileError.NotFound, "Profile not found");
		}

		var sourceFolders = _folderCache.GetFoldersByProfileId(id)
			.OrderBy(folder => folder.CreatedAt)
			.ThenBy(folder => folder.Id)
			.ToList();

		var idMap = new Dictionary<Guid, Guid> { [source.Id] = Guid.NewGuid() };
		foreach (var folder in sourceFolders)
		{
			idMap[folder.Id] = Guid.NewGuid();
			foreach (var widget in folder.Widgets)
			{
				idMap[widget.Id] = Guid.NewGuid();
			}
		}

		var existing = _profileCache.GetAll();
		var createdAt = DateTime.UtcNow;
		var profile = new ProfileEntity
		{
			Id = idMap[source.Id],
			Name = NextCopyName(string.IsNullOrWhiteSpace(name) ? $"{source.Name} (copy)" : name.Trim(), existing),
			Order = existing.Count > 0 ? existing.Max(p => p.Order) + 1 : 0,
			LayoutType = source.LayoutType,
			DefaultRows = source.DefaultRows,
			DefaultColumns = source.DefaultColumns,
			DefaultBackgroundColor = source.DefaultBackgroundColor,
			DefaultWidgetSpacing = source.DefaultWidgetSpacing,
			DefaultWidgetBorderRadius = source.DefaultWidgetBorderRadius,
			DefaultEmptyCellStyle = source.DefaultEmptyCellStyle,
			CreatedAt = createdAt
		};

		var folders = new List<FolderEntity>(sourceFolders.Count);
		var sourceWidgetIdsByCopyId = new Dictionary<Guid, Guid>();
		for (var index = 0; index < sourceFolders.Count; index++)
		{
			var sourceFolder = sourceFolders[index];
			var folder = new FolderEntity
			{
				Id = idMap[sourceFolder.Id],
				ProfileId = profile.Id,
				Name = sourceFolder.Name,
				ParentId = sourceFolder.ParentId is { } parentId && idMap.TryGetValue(parentId, out var copiedParentId)
					? copiedParentId
					: sourceFolder.ParentId,
				Order = sourceFolder.Order,
				Rows = sourceFolder.Rows,
				Columns = sourceFolder.Columns,
				BackgroundColor = sourceFolder.BackgroundColor,
				WidgetSpacing = sourceFolder.WidgetSpacing,
				WidgetBorderRadius = sourceFolder.WidgetBorderRadius,
				EmptyCellStyle = sourceFolder.EmptyCellStyle,
				IsDefault = sourceFolder.IsDefault,
				ViewId = sourceFolder.ViewId,
				ViewConfiguration = PortableGuidRemapper.Remap(sourceFolder.ViewConfiguration, idMap),
				// An enabled focus rule may exist only once per device and application across all
				// profiles, so the copy starts without the original's rules.
				FocusRules = [],
				CreatedAt = createdAt.AddTicks(index)
			};

			foreach (var sourceWidget in sourceFolder.Widgets)
			{
				var widget = new WidgetEntity
				{
					Id = idMap[sourceWidget.Id],
					FolderId = folder.Id,
					Type = sourceWidget.Type,
					PositionX = sourceWidget.PositionX,
					PositionY = sourceWidget.PositionY,
					Width = sourceWidget.Width,
					Height = sourceWidget.Height,
					Data = PortableGuidRemapper.Remap(
						await _widgetSecretCloner.CloneReferencedSecrets(sourceWidget.Data),
						idMap),
					IsPinned = sourceWidget.IsPinned,
					PinScope = sourceWidget.PinScope,
					CreatedAt = createdAt
				};
				folder.Widgets.Add(widget);
				sourceWidgetIdsByCopyId[widget.Id] = sourceWidget.Id;
			}

			folders.Add(folder);
		}

		await _profileCache.AddOrUpdateAggregate(profile, folders);

		foreach (var (copyId, sourceId) in sourceWidgetIdsByCopyId)
		{
			await _widgetVariableCloner.Clone(sourceId, copyId);
		}

		await _mediator.Publish(new ProfileCreatedNotification(profile));
		foreach (var folder in folders)
		{
			await _mediator.Publish(new FolderCreatedNotification(folder));
			foreach (var widget in folder.Widgets)
			{
				await _mediator.Publish(new WidgetCreatedNotification(widget));
			}
		}

		return Result.Ok<ProfileEntity, ProfileError>(profile);
	}

	private static string NextCopyName(string baseName, IReadOnlyCollection<ProfileEntity> existing)
	{
		var taken = existing.Select(profile => profile.Name).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
		if (!taken.Contains(baseName))
		{
			return baseName;
		}

		for (var suffix = 2;; suffix++)
		{
			var candidate = $"{baseName} {suffix}";
			if (!taken.Contains(candidate))
			{
				return candidate;
			}
		}
	}
}
