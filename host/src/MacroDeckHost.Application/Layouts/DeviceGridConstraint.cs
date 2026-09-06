namespace MacroDeckHost.Application.Layouts;

/// <summary>
/// What the devices that claim a profile (via <c>DeviceEntity.StartupProfileId</c>) say about the grid
/// that profile - or the folder currently on screen - is allowed to be. Grid-agnostic: it carries no
/// opinion about a specific candidate size, so the same value serves both display (compare against the
/// current grid) and edit validation (compare against a candidate one).
///
/// <para>
/// The two Honoured flags are not locks. Widget spacing and corner radius are not fixed to a value by
/// hardware - they simply do not change what a physical surface renders, so they default to true and go
/// false only when a claiming layout says the setting has no effect there.
/// </para>
///
/// <para>
/// <see cref="CustomFolderViewsSupported" /> is a third kind again: not fidelity, but whether the surface
/// can show a folder view at all. It defaults to true for the same reason the others do - a profile no
/// device claims, or one whose layout declared no visuals, must not lose the option.
/// </para>
/// </summary>
public sealed record DeviceGridConstraint(
	bool RowsLocked,
	bool ColumnsLocked,
	int Rows,
	int Columns,
	int MinRows,
	int MaxRows,
	int MinColumns,
	int MaxColumns,
	string? LayoutId,
	string? LayoutName,
	string? DeviceName,
	bool HasConflict,
	IReadOnlyList<string> ConflictingDeviceNames,
	bool WidgetSpacingHonoured = true,
	bool CornerRadiusHonoured = true,
	bool CustomFolderViewsSupported = true);
