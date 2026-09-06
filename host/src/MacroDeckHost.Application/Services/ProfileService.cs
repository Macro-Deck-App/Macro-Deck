using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Application.Persistence.Repositories;
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

	public ProfileService(
		IProfileCache profileCache,
		IFolderCache folderCache,
		IDeviceRepository deviceRepository,
		IMediator mediator)
	{
		_profileCache = profileCache;
		_folderCache = folderCache;
		_deviceRepository = deviceRepository;
		_mediator = mediator;
	}

	public async Task<Result<ProfileEntity, ProfileError>> Create(
		string name,
		ProfileLayoutType? layoutType = null,
		int? defaultRows = null,
		int? defaultColumns = null,
		string? defaultBackgroundColor = null,
		int? defaultWidgetSpacing = null,
		int? defaultWidgetBorderRadius = null)
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
		int? defaultWidgetBorderRadius)
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
}
