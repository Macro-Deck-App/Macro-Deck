using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Application.Ui.Transport.Messages.Profiles;
using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Ui.Handlers;

public static class ProfileDtoMapper
{
	public static Profile MapJsonProfile(ProfileEntity entity) => MapJsonProfile(entity, constraint: null);

	public static Profile MapJsonProfile(ProfileEntity entity, DeviceGridConstraint? constraint)
		=> new()
		{
			Id = entity.Id.ToString(),
			Name = entity.Name,
			Order = entity.Order,
			LayoutType = entity.LayoutType.ToString(),
			IsVirtual = false,
			SourceIntegrationId = null,
			Layout = new ProfileLayout
			{
				Rows = entity.DefaultRows,
				Columns = entity.DefaultColumns,
				RowsLocked = false,
				ColumnsLocked = false,
				Constraint = MapConstraint(constraint),
				Compatibility = MapCompatibility(constraint, entity.DefaultRows, entity.DefaultColumns)
			},
			DefaultRows = entity.DefaultRows,
			DefaultColumns = entity.DefaultColumns,
			DefaultBackgroundColor = entity.DefaultBackgroundColor,
			DefaultWidgetSpacing = entity.DefaultWidgetSpacing,
			DefaultWidgetBorderRadius = entity.DefaultWidgetBorderRadius
		};

	private static ProfileLayoutConstraint? MapConstraint(DeviceGridConstraint? constraint)
		=> constraint is null
			? null
			: new ProfileLayoutConstraint
			{
				Rows = constraint.Rows,
				Columns = constraint.Columns,
				MinRows = constraint.MinRows,
				MaxRows = constraint.MaxRows,
				MinColumns = constraint.MinColumns,
				MaxColumns = constraint.MaxColumns,
				RowsLocked = constraint.RowsLocked,
				ColumnsLocked = constraint.ColumnsLocked,
				LayoutId = constraint.LayoutId,
				LayoutName = constraint.LayoutName,
				DeviceName = constraint.DeviceName,
				WidgetSpacingHonoured = constraint.WidgetSpacingHonoured,
				CornerRadiusHonoured = constraint.CornerRadiusHonoured,
				CustomFolderViewsSupported = constraint.CustomFolderViewsSupported
			};

	private static ProfileLayoutCompatibility? MapCompatibility(
		DeviceGridConstraint? constraint,
		int currentRows,
		int currentColumns)
	{
		if (constraint is null)
		{
			return null;
		}

		if (constraint.HasConflict)
		{
			return new ProfileLayoutCompatibility
			{
				Status = "conflictingDevices",
				DeviceNames = constraint.ConflictingDeviceNames
			};
		}

		if (constraint.ExceedsLockedLayout(currentRows, currentColumns))
		{
			return new ProfileLayoutCompatibility { Status = "exceedsLayout", DeviceNames = [] };
		}

		return new ProfileLayoutCompatibility { Status = "ok", DeviceNames = [] };
	}
}
