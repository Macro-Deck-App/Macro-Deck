using MacroDeck.Sdk.Layouts;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Layouts;

/// <summary>
/// Turns the devices that claim a profile into the grid constraint they place on it (issue #384). Pure
/// and synchronous: callers own fetching the claiming devices (<c>IDeviceRepository.GetByStartupProfileId</c>)
/// and deciding what to do with the result.
/// </summary>
public static class DeviceLayoutConstraintResolver
{
	public static DeviceGridConstraint? Resolve(IReadOnlyList<DeviceEntity> claimingDevices)
	{
		ArgumentNullException.ThrowIfNull(claimingDevices);

		var resolved = new List<(DeviceEntity Device, LayoutDescriptor Layout, LayoutGrid Grid)>();
		foreach (var device in claimingDevices.OrderBy(d => d.Id))
		{
			if (!LayoutSnapshotSerializer.TryDeserialize(device.LayoutSnapshot, out var layout))
			{
				continue;
			}

			// PrimaryGrid is null for a layout with zero or several grid regions - such a layout
			// constrains nothing (issue #384's explicit rule), so the device is skipped rather than
			// treated as claiming an empty grid.
			if (layout.PrimaryGrid?.Grid is not { } grid)
			{
				continue;
			}

			resolved.Add((device, layout, grid));
		}

		if (resolved.Count == 0)
		{
			return null;
		}

		var fixedEntries = resolved.Where(entry => entry.Grid.RowsLocked && entry.Grid.ColumnsLocked).ToList();
		var distinctPairs = fixedEntries.Select(entry => (entry.Grid.Rows, entry.Grid.Columns))
			.Distinct()
			.ToList();

		var spans = ResolveWidgetSpans(resolved);

		if (distinctPairs.Count == 1)
		{
			var (rows, columns) = distinctPairs[0];
			var representative = fixedEntries[0];
			return WithWidgetSpans(new DeviceGridConstraint(true,
					true,
					rows,
					columns,
					rows,
					rows,
					columns,
					columns,
					representative.Device.LayoutReference,
					representative.Layout.Name,
					representative.Device.Name,
					false,
					[],
					Honoured(representative.Layout).Spacing,
					Honoured(representative.Layout).CornerRadius,
					Honoured(representative.Layout).CustomFolderViews),
				spans);
		}

		if (distinctPairs.Count > 1)
		{
			return WithWidgetSpans(Unlocked(true,
					[.. fixedEntries.Select(entry => entry.Device.Name).Distinct(StringComparer.Ordinal)]),
				spans);
		}

		// No claiming device declares a fixed grid, so what is left is configurable layouts contributing
		// bounds without locking a free axis. Several claiming devices may still disagree about those
		// bounds, and picking one of them would be the same silent guess the fixed-grid branch above
		// refuses to make - so a disagreement yields no constraint at all rather than an arbitrary one.
		// There is nothing to warn about: no axis is locked either way, so the grid stays editable.
		var distinctBounds = resolved
			.Select(entry => (entry.Grid.RowsLocked, entry.Grid.ColumnsLocked, entry.Grid.Rows,
				entry.Grid.Columns, entry.Grid.MinRows, entry.Grid.MaxRows, entry.Grid.MinColumns,
				entry.Grid.MaxColumns))
			.Distinct()
			.Count();

		if (distinctBounds > 1)
		{
			return spans.Columns is null && spans.Rows is null ? null : WithWidgetSpans(SpanOnly(), spans);
		}

		var single = resolved[0];
		var singleGrid = single.Grid;
		return WithWidgetSpans(new DeviceGridConstraint(singleGrid.RowsLocked,
				singleGrid.ColumnsLocked,
				singleGrid.Rows,
				singleGrid.Columns,
				singleGrid.RowsLocked ? singleGrid.Rows : singleGrid.MinRows,
				singleGrid.RowsLocked ? singleGrid.Rows : singleGrid.MaxRows,
				singleGrid.ColumnsLocked ? singleGrid.Columns : singleGrid.MinColumns,
				singleGrid.ColumnsLocked ? singleGrid.Columns : singleGrid.MaxColumns,
				single.Device.LayoutReference,
				single.Layout.Name,
				single.Device.Name,
				false,
				[],
				Honoured(single.Layout).Spacing,
				Honoured(single.Layout).CornerRadius,
				Honoured(single.Layout).CustomFolderViews),
			spans);
	}

	private static DeviceGridConstraint Unlocked(bool hasConflict, IReadOnlyList<string> conflictingDeviceNames)
	{
		return new DeviceGridConstraint(false,
			false,
			0,
			0,
			1,
			int.MaxValue,
			1,
			int.MaxValue,
			null,
			null,
			null,
			hasConflict,
			conflictingDeviceNames);
	}

	private static DeviceGridConstraint SpanOnly()
	{
		return new DeviceGridConstraint(false,
			false,
			0,
			0,
			GridDefaults.MinRows,
			GridDefaults.MaxRows,
			GridDefaults.MinColumns,
			GridDefaults.MaxColumns,
			null,
			null,
			null,
			false,
			[],
			true,
			true,
			true,
			null,
			null,
			null,
			null,
			false);
	}

	private static WidgetSpanLimits ResolveWidgetSpans(
		IReadOnlyList<(DeviceEntity Device, LayoutDescriptor Layout, LayoutGrid Grid)> resolved)
	{
		var columns = Strictest(resolved, entry => entry.Grid.MaxWidgetColumns);
		var rows = Strictest(resolved, entry => entry.Grid.MaxWidgetRows);
		return new WidgetSpanLimits(columns.Limit, columns.DeviceName, rows.Limit, rows.DeviceName);
	}

	private static (int? Limit, string? DeviceName) Strictest(
		IReadOnlyList<(DeviceEntity Device, LayoutDescriptor Layout, LayoutGrid Grid)> resolved,
		Func<(DeviceEntity Device, LayoutDescriptor Layout, LayoutGrid Grid), int?> limitOf)
	{
		(int? Limit, string? DeviceName) strictest = (null, null);
		foreach (var entry in resolved)
		{
			if (limitOf(entry) is not { } declared)
			{
				continue;
			}

			var limit = Math.Max(1, declared);
			if (strictest.Limit is null || limit < strictest.Limit)
			{
				strictest = (limit, entry.Device.Name);
			}
		}

		return strictest;
	}

	private static DeviceGridConstraint WithWidgetSpans(DeviceGridConstraint constraint, WidgetSpanLimits spans)
	{
		return constraint with
		{
			MaxWidgetColumns = spans.Columns,
			MaxWidgetRows = spans.Rows,
			MaxWidgetColumnsDeviceName = spans.ColumnsDeviceName,
			MaxWidgetRowsDeviceName = spans.RowsDeviceName
		};
	}

	/// <summary>Whether a candidate grid edit is allowed under <paramref name="constraint" />. Only the
	/// axes actually being changed are checked - an edit that leaves rows or columns untouched never
	/// trips this, even if the profile's current size already sits outside the constraint.</summary>
	/// <summary>
	/// A region's own visuals win over the layout-wide ones; a layout that declares neither says nothing,
	/// and saying nothing must not read as "ignores your spacing" - that would disable settings for every
	/// provider that simply did not fill the block in.
	/// </summary>
	private static (bool Spacing, bool CornerRadius, bool CustomFolderViews) Honoured(LayoutDescriptor layout)
	{
		var visuals = layout.PrimaryGrid?.Visuals ?? layout.Capabilities?.Visuals;

		return visuals is null
			? (true, true, true)
			: (visuals.WidgetSpacing, visuals.CornerRadius, visuals.CustomFolderViews);
	}

	public static bool AllowsEdit(
		this DeviceGridConstraint constraint,
		int? candidateRows,
		int? candidateColumns)
	{
		if (constraint.HasConflict || !constraint.ConstrainsGrid)
		{
			return true;
		}

		if (candidateRows.HasValue)
		{
			var withinBounds = constraint.RowsLocked
				? candidateRows.Value == constraint.Rows
				: candidateRows.Value >= constraint.MinRows && candidateRows.Value <= constraint.MaxRows;
			if (!withinBounds)
			{
				return false;
			}
		}

		if (candidateColumns.HasValue)
		{
			var withinBounds = constraint.ColumnsLocked
				? candidateColumns.Value == constraint.Columns
				: candidateColumns.Value >= constraint.MinColumns && candidateColumns.Value <= constraint.MaxColumns;
			if (!withinBounds)
			{
				return false;
			}
		}

		return true;
	}

	public static bool AllowsWidgetSpan(
		this DeviceGridConstraint constraint,
		int width,
		int height,
		int? currentWidth,
		int? currentHeight)
	{
		return WithinSpan(width, currentWidth, constraint.MaxWidgetColumns)
			&& WithinSpan(height, currentHeight, constraint.MaxWidgetRows);
	}

	private static bool WithinSpan(int candidate, int? current, int? limit)
	{
		return limit is not { } max || candidate <= max || current is { } existing && candidate <= existing;
	}

	/// <summary>
	/// Whether a grid already sitting at <paramref name="currentRows" /> x <paramref name="currentColumns" />
	/// is bigger, on either axis, than the single fixed grid <paramref name="constraint" /> locks to. A
	/// display-only warning: nothing about it resizes the grid or blocks an edit that leaves rows and
	/// columns alone.
	/// </summary>
	public static bool ExceedsLockedLayout(this DeviceGridConstraint constraint, int currentRows, int currentColumns)
	{
		return !constraint.HasConflict
			&& constraint.RowsLocked
			&& constraint.ColumnsLocked
			&& (currentRows > constraint.Rows || currentColumns > constraint.Columns);
	}

	private readonly record struct WidgetSpanLimits(
		int? Columns,
		string? ColumnsDeviceName,
		int? Rows,
		string? RowsDeviceName);
}
