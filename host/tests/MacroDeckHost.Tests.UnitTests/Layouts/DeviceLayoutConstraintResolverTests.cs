using MacroDeck.Sdk.Layouts;
using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Tests.UnitTests.Layouts;

/// <summary>
/// The pure grid-lock rule of issue #384: what the devices claiming a profile - via
/// <c>DeviceEntity.StartupProfileId</c> - say a profile's grid is allowed to be.
/// </summary>
public class DeviceLayoutConstraintResolverTests
{
	[Test]
	public void NoClaimingDevices_ProducesNoConstraint()
		=> Assert.That(DeviceLayoutConstraintResolver.Resolve([]), Is.Null);

	[Test]
	public void OneFixedDevice_LocksToItsRowsAndColumns()
	{
		var device = Device("Deck A", FixedGrid(5, 3));

		var constraint = DeviceLayoutConstraintResolver.Resolve([device]);

		Assert.Multiple(() =>
		{
			Assert.That(constraint, Is.Not.Null);
			Assert.That(constraint!.RowsLocked, Is.True);
			Assert.That(constraint.ColumnsLocked, Is.True);
			Assert.That(constraint.Rows, Is.EqualTo(5));
			Assert.That(constraint.Columns, Is.EqualTo(3));
			Assert.That(constraint.HasConflict, Is.False);
			Assert.That(constraint.DeviceName, Is.EqualTo("Deck A"));
		});
	}

	[Test]
	public void TwoDevicesWithTheSameFixedGrid_StillLock_WithNoConflict()
	{
		var first = Device("Deck A", FixedGrid(5, 3));
		var second = Device("Deck B", FixedGrid(5, 3));

		var constraint = DeviceLayoutConstraintResolver.Resolve([first, second]);

		Assert.Multiple(() =>
		{
			Assert.That(constraint!.RowsLocked, Is.True);
			Assert.That(constraint.ColumnsLocked, Is.True);
			Assert.That(constraint.Rows, Is.EqualTo(5));
			Assert.That(constraint.Columns, Is.EqualTo(3));
			Assert.That(constraint.HasConflict, Is.False);
		});
	}

	[Test]
	public void TwoDevicesWithDifferentFixedGrids_DoNotLock_AndNameBothDevices_RegardlessOfOrder()
	{
		var a = Device("Deck A", FixedGrid(5, 3));
		var b = Device("Deck B", FixedGrid(4, 8));

		var firstOrder = DeviceLayoutConstraintResolver.Resolve([a, b]);
		var secondOrder = DeviceLayoutConstraintResolver.Resolve([b, a]);
		var expectedNames = new[] { "Deck A", "Deck B" };
		var orderings = new[] { firstOrder, secondOrder };

		foreach (var constraint in orderings)
		{
			Assert.Multiple(() =>
			{
				Assert.That(constraint!.HasConflict, Is.True);
				Assert.That(constraint.RowsLocked, Is.False);
				Assert.That(constraint.ColumnsLocked, Is.False);
				Assert.That(constraint.ConflictingDeviceNames, Is.EquivalentTo(expectedNames));
			});
		}
	}

	[Test]
	public void SingleConfigurableDevice_ContributesBoundsOnly_AndNeverLocksAFreeAxis()
	{
		var device = Device("Client",
			ConfigurableGrid(rows: 3, columns: 5, minRows: 1, maxRows: 8, minColumns: 1, maxColumns: 12));

		var constraint = DeviceLayoutConstraintResolver.Resolve([device]);

		Assert.Multiple(() =>
		{
			Assert.That(constraint!.RowsLocked, Is.False);
			Assert.That(constraint.ColumnsLocked, Is.False);
			Assert.That(constraint.MinRows, Is.EqualTo(1));
			Assert.That(constraint.MaxRows, Is.EqualTo(8));
			Assert.That(constraint.MinColumns, Is.EqualTo(1));
			Assert.That(constraint.MaxColumns, Is.EqualTo(12));
		});
	}

	[Test]
	public void TwoConfigurableDevicesWithDifferentBounds_ProduceNoConstraint_RatherThanTheFirstOnes()
	{
		var narrow = Device("Client A",
			ConfigurableGrid(rows: 3, columns: 5, minRows: 1, maxRows: 4, minColumns: 1, maxColumns: 6));
		var wide = Device("Client B",
			ConfigurableGrid(rows: 3, columns: 5, minRows: 1, maxRows: 8, minColumns: 1, maxColumns: 12));

		Assert.Multiple(() =>
		{
			Assert.That(DeviceLayoutConstraintResolver.Resolve([narrow, wide]), Is.Null);
			Assert.That(DeviceLayoutConstraintResolver.Resolve([wide, narrow]), Is.Null);
		});
	}

	[Test]
	public void TwoConfigurableDevicesWithIdenticalBounds_StillContributeThem()
	{
		var first = Device("Client A",
			ConfigurableGrid(rows: 3, columns: 5, minRows: 1, maxRows: 8, minColumns: 1, maxColumns: 12));
		var second = Device("Client B",
			ConfigurableGrid(rows: 3, columns: 5, minRows: 1, maxRows: 8, minColumns: 1, maxColumns: 12));

		var constraint = DeviceLayoutConstraintResolver.Resolve([first, second]);

		Assert.Multiple(() =>
		{
			Assert.That(constraint!.MaxRows, Is.EqualTo(8));
			Assert.That(constraint.MaxColumns, Is.EqualTo(12));
			Assert.That(constraint.RowsLocked, Is.False);
		});
	}

	[Test]
	public void ASurfaceThatIgnoresSpacingAndRadius_ReportsThemUnhonoured_WithoutLockingThemToAValue()
	{
		var layout = FixedGrid(5, 3) with
		{
			Regions =
			[
				FixedGrid(5, 3).Regions[0] with
				{
					Visuals = new LayoutVisualCapabilities
						{ StaticIcons = true, WidgetSpacing = false, CornerRadius = false }
				}
			]
		};

		var constraint = DeviceLayoutConstraintResolver.Resolve([Device("Deck A", layout)])!;

		Assert.Multiple(() =>
		{
			Assert.That(constraint.WidgetSpacingHonoured, Is.False);
			Assert.That(constraint.CornerRadiusHonoured, Is.False);

			// Not a lock: the grid is what the device fixes, spacing is merely inert there.
			Assert.That(constraint.RowsLocked, Is.True);
		});
	}

	[Test]
	public void ALayoutThatDeclaresNoVisuals_LeavesSpacingAndRadiusHonoured()
	{
		var constraint = DeviceLayoutConstraintResolver.Resolve([Device("Deck A", FixedGrid(5, 3))])!;

		Assert.Multiple(() =>
		{
			Assert.That(constraint.WidgetSpacingHonoured, Is.True);
			Assert.That(constraint.CornerRadiusHonoured, Is.True);
			Assert.That(constraint.CustomFolderViewsSupported, Is.True);
		});
	}

	/// <summary>
	/// A surface the host rasterises a key grid for cannot draw a Macro Deck UI tree at all, so a folder
	/// set to a provider's view would simply be blank on it. Unlike the two Honoured flags this is not
	/// about fidelity - it is why the editor does not offer the choice for such a profile.
	/// </summary>
	[Test]
	public void ASurfaceThatCannotRenderATree_ReportsCustomFolderViewsUnsupported()
	{
		var layout = FixedGrid(5, 3) with
		{
			Regions =
			[
				FixedGrid(5, 3).Regions[0] with
				{
					Visuals = new LayoutVisualCapabilities { StaticIcons = true, CustomFolderViews = false }
				}
			]
		};

		var constraint = DeviceLayoutConstraintResolver.Resolve([Device("Deck A", layout)])!;

		Assert.That(constraint.CustomFolderViewsSupported, Is.False);
	}

	[Test]
	public void AFullyCapableSurface_SupportsCustomFolderViews()
	{
		var layout = FixedGrid(5, 3) with
		{
			Regions = [FixedGrid(5, 3).Regions[0] with { Visuals = LayoutVisualCapabilities.Full }]
		};

		var constraint = DeviceLayoutConstraintResolver.Resolve([Device("Deck A", layout)])!;

		Assert.That(constraint.CustomFolderViewsSupported, Is.True);
	}

	[Test]
	public void UnresolvableLayoutReference_IsIgnored_LikeTheDeviceDoesNotClaimAGrid()
	{
		var device = Device("Deck A", layoutSnapshot: null);

		Assert.That(DeviceLayoutConstraintResolver.Resolve([device]), Is.Null);
	}

	[Test]
	public void ACorruptSnapshot_IsTreatedAsAbsent_NeverAsAFault()
	{
		var device = Device("Deck A", layoutSnapshot: "{ not json");

		Assert.That(DeviceLayoutConstraintResolver.Resolve([device]), Is.Null);
	}

	[Test]
	public void ALayoutWithNoGridRegion_ConstrainsNothing()
	{
		var layout = new LayoutDescriptor("pedal-board",
			"Pedal Board",
			[
				new LayoutRegion { Id = "pedals", Kind = LayoutRegionKinds.Pedal, Count = 3 }
			]);
		var device = Device("Pedals", layoutSnapshot: LayoutSnapshotSerializer.Serialize(layout));

		Assert.That(DeviceLayoutConstraintResolver.Resolve([device]), Is.Null);
	}

	[Test]
	public void ALayoutWithTwoGridRegions_ConstrainsNothing()
	{
		var layout = new LayoutDescriptor("dual",
			"Dual Grid",
			[
				new LayoutRegion
					{ Id = "left", Kind = LayoutRegionKinds.Grid, Grid = new LayoutGrid { Rows = 3, Columns = 3 } },
				new LayoutRegion
					{ Id = "right", Kind = LayoutRegionKinds.Grid, Grid = new LayoutGrid { Rows = 3, Columns = 3 } }
			]);
		var device = Device("Dual", layoutSnapshot: LayoutSnapshotSerializer.Serialize(layout));

		Assert.That(DeviceLayoutConstraintResolver.Resolve([device]), Is.Null);
	}

	[Test]
	public void AllowsEdit_RejectsAChangeToTheLockedAxis_ButAllowsAnEditThatLeavesItAlone()
	{
		var constraint = DeviceLayoutConstraintResolver.Resolve([Device("Deck A", FixedGrid(5, 3))])!;

		Assert.Multiple(() =>
		{
			Assert.That(constraint.AllowsEdit(candidateRows: 5, candidateColumns: 3), Is.True);
			Assert.That(constraint.AllowsEdit(candidateRows: 4, candidateColumns: null), Is.False);
			Assert.That(constraint.AllowsEdit(candidateRows: null, candidateColumns: null), Is.True);
		});
	}

	[Test]
	public void ExceedsLockedLayout_ReportsWhenTheCurrentGridIsBiggerThanTheLock_ButAllowsEditStillHoldsWhenUntouched()
	{
		var constraint = DeviceLayoutConstraintResolver.Resolve([Device("Deck A", FixedGrid(3, 3))])!;

		Assert.Multiple(() =>
		{
			Assert.That(constraint.ExceedsLockedLayout(currentRows: 5, currentColumns: 5), Is.True);
			// An edit that does not touch rows/columns must still be allowed even though the profile
			// already sits outside the lock (issue #384: nothing is silently resized or blocked on load).
			Assert.That(constraint.AllowsEdit(candidateRows: null, candidateColumns: null), Is.True);
		});
	}

	[Test]
	public void AConflict_NeverExceedsLayout_AndNeverBlocksAnEdit()
	{
		var constraint
			= DeviceLayoutConstraintResolver.Resolve([Device("A", FixedGrid(5, 3)), Device("B", FixedGrid(4, 8))])!;

		Assert.Multiple(() =>
		{
			Assert.That(constraint.ExceedsLockedLayout(currentRows: 100, currentColumns: 100), Is.False);
			Assert.That(constraint.AllowsEdit(candidateRows: 1, candidateColumns: 1), Is.True);
		});
	}

	private static DeviceEntity Device(string name, LayoutDescriptor layout)
		=> Device(name, LayoutSnapshotSerializer.Serialize(layout));

	private static DeviceEntity Device(string name, string? layoutSnapshot)
		=> new()
		{
			Id = Guid.NewGuid(),
			SecretHash = string.Empty,
			Name = name,
			ClientType = DeviceClientType.Provider,
			FormFactor = DeviceFormFactor.Unknown,
			LayoutReference = "owner::layout",
			LayoutSnapshot = layoutSnapshot,
			CreatedAt = DateTime.UtcNow,
			LastSeenAt = DateTime.UtcNow
		};

	private static LayoutDescriptor FixedGrid(int rows, int columns)
		=> new("fixed",
			"Fixed Grid",
			[
				new LayoutRegion
				{
					Id = "grid", Kind = LayoutRegionKinds.Grid, Grid = new LayoutGrid { Rows = rows, Columns = columns }
				}
			]);

	private static LayoutDescriptor ConfigurableGrid(
		int rows,
		int columns,
		int minRows,
		int maxRows,
		int minColumns,
		int maxColumns)
		=> new("configurable",
			"Configurable Grid",
			[
				new LayoutRegion
				{
					Id = "grid",
					Kind = LayoutRegionKinds.Grid,
					Grid = new LayoutGrid
					{
						Rows = rows,
						Columns = columns,
						IsConfigurable = true,
						MinRows = minRows,
						MaxRows = maxRows,
						MinColumns = minColumns,
						MaxColumns = maxColumns
					}
				}
			]);
}
