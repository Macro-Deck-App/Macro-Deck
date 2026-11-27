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
	{
		Assert.That(DeviceLayoutConstraintResolver.Resolve([]), Is.Null);
	}

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
			ConfigurableGrid(3, 5, 1, 8, 1, 12));

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
			ConfigurableGrid(3, 5, 1, 4, 1, 6));
		var wide = Device("Client B",
			ConfigurableGrid(3, 5, 1, 8, 1, 12));

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
			ConfigurableGrid(3, 5, 1, 8, 1, 12));
		var second = Device("Client B",
			ConfigurableGrid(3, 5, 1, 8, 1, 12));

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

	[TestCase(true, 8, false)]
	[TestCase(false, 8, false)]
	[TestCase(true, null, true)]
	public void AKeyImageGridThatFixesItsRadius_ReportsTheProfileRadiusUnhonoured(
		bool declaresVisuals,
		int? fixedRadius,
		bool honoured)
	{
		var grid = FixedGrid(3, 5).Regions[0];
		var layout = FixedGrid(3, 5) with
		{
			Regions =
			[
				grid with
				{
					Grid = grid.Grid! with
					{
						KeySize = new LayoutKeySize(72, 72),
						KeyImages = new LayoutKeyImageFormat
						{
							Encoding = KeyImageEncodings.Jpeg, CornerRadius = fixedRadius
						}
					},
					Visuals = declaresVisuals ? LayoutVisualCapabilities.Full : null
				}
			]
		};

		var constraint = DeviceLayoutConstraintResolver.Resolve([Device("Deck A", layout)])!;

		Assert.Multiple(() =>
		{
			Assert.That(constraint.CornerRadiusHonoured, Is.EqualTo(honoured));
			Assert.That(constraint.WidgetSpacingHonoured, Is.True);
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
		var device = Device("Deck A", "{ not json");

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
		var device = Device("Pedals", LayoutSnapshotSerializer.Serialize(layout));

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
		var device = Device("Dual", LayoutSnapshotSerializer.Serialize(layout));

		Assert.That(DeviceLayoutConstraintResolver.Resolve([device]), Is.Null);
	}

	[Test]
	public void AllowsEdit_RejectsAChangeToTheLockedAxis_ButAllowsAnEditThatLeavesItAlone()
	{
		var constraint = DeviceLayoutConstraintResolver.Resolve([Device("Deck A", FixedGrid(5, 3))])!;

		Assert.Multiple(() =>
		{
			Assert.That(constraint.AllowsEdit(5, 3), Is.True);
			Assert.That(constraint.AllowsEdit(4, null), Is.False);
			Assert.That(constraint.AllowsEdit(null, null), Is.True);
		});
	}

	[Test]
	public void ExceedsLockedLayout_ReportsWhenTheCurrentGridIsBiggerThanTheLock_ButAllowsEditStillHoldsWhenUntouched()
	{
		var constraint = DeviceLayoutConstraintResolver.Resolve([Device("Deck A", FixedGrid(3, 3))])!;

		Assert.Multiple(() =>
		{
			Assert.That(constraint.ExceedsLockedLayout(5, 5), Is.True);
			// An edit that does not touch rows/columns must still be allowed even though the profile
			// already sits outside the lock (issue #384: nothing is silently resized or blocked on load).
			Assert.That(constraint.AllowsEdit(null, null), Is.True);
		});
	}

	[Test]
	public void AConflict_NeverExceedsLayout_AndNeverBlocksAnEdit()
	{
		var constraint
			= DeviceLayoutConstraintResolver.Resolve([Device("A", FixedGrid(5, 3)), Device("B", FixedGrid(4, 8))])!;

		Assert.Multiple(() =>
		{
			Assert.That(constraint.ExceedsLockedLayout(100, 100), Is.False);
			Assert.That(constraint.AllowsEdit(1, 1), Is.True);
		});
	}

	[Test]
	public void ADeviceThatKeepsWidgetsToOneKey_LimitsBothAxes_AndNamesItself()
	{
		var constraint
			= DeviceLayoutConstraintResolver.Resolve([Device("Stream Deck", WithSpans(FixedGrid(3, 5), 1, 1))])!;

		Assert.Multiple(() =>
		{
			Assert.That(constraint.MaxWidgetColumns, Is.EqualTo(1));
			Assert.That(constraint.MaxWidgetRows, Is.EqualTo(1));
			Assert.That(constraint.MaxWidgetColumnsDeviceName, Is.EqualTo("Stream Deck"));
			Assert.That(constraint.MaxWidgetRowsDeviceName, Is.EqualTo("Stream Deck"));
		});
	}

	[Test]
	public void ALayoutWithoutSpanLimits_LeavesWidgetsUnconstrained()
	{
		var constraint = DeviceLayoutConstraintResolver.Resolve([Device("Deck A", FixedGrid(3, 5))])!;

		Assert.Multiple(() =>
		{
			Assert.That(constraint.MaxWidgetColumns, Is.Null);
			Assert.That(constraint.MaxWidgetRows, Is.Null);
			Assert.That(constraint.AllowsWidgetSpan(5, 3, null, null), Is.True);
		});
	}

	[Test]
	public void TheStrictestLimitPerAxisWins_NamingTheDeviceEachOneComesFrom()
	{
		var columnsLimited = Device("Narrow Pad", WithSpans(FixedGrid(3, 5), 1, null));
		var rowsLimited = Device("Short Pad", WithSpans(FixedGrid(3, 5), 4, 2));
		var unlimited = Device("Open Pad", FixedGrid(3, 5));

		var constraint = DeviceLayoutConstraintResolver.Resolve([unlimited, rowsLimited, columnsLimited])!;

		Assert.Multiple(() =>
		{
			Assert.That(constraint.MaxWidgetColumns, Is.EqualTo(1));
			Assert.That(constraint.MaxWidgetColumnsDeviceName, Is.EqualTo("Narrow Pad"));
			Assert.That(constraint.MaxWidgetRows, Is.EqualTo(2));
			Assert.That(constraint.MaxWidgetRowsDeviceName, Is.EqualTo("Short Pad"));
		});
	}

	[Test]
	public void DevicesWhoseGridsConflict_StillKeepTheirSpanLimit()
	{
		var small = Device("Stream Deck Mini", WithSpans(FixedGrid(2, 3), 1, 1));
		var large = Device("Stream Deck XL", WithSpans(FixedGrid(4, 8), 1, 1));

		var constraint = DeviceLayoutConstraintResolver.Resolve([small, large])!;

		Assert.Multiple(() =>
		{
			Assert.That(constraint.HasConflict, Is.True);
			Assert.That(constraint.MaxWidgetColumns, Is.EqualTo(1));
			Assert.That(constraint.MaxWidgetRows, Is.EqualTo(1));
			Assert.That(constraint.AllowsWidgetSpan(2, 1, null, null), Is.False);
		});
	}

	[Test]
	public void ConfigurableDevicesThatDisagree_StillKeepTheirSpanLimit_WithoutCappingTheGrid()
	{
		var narrow = Device("Client A", WithSpans(ConfigurableGrid(3, 5, 1, 4, 1, 6), 1, 1));
		var wide = Device("Client B", ConfigurableGrid(3, 5, 1, 8, 1, 12));

		var constraint = DeviceLayoutConstraintResolver.Resolve([narrow, wide])!;

		Assert.Multiple(() =>
		{
			Assert.That(constraint.MaxWidgetColumns, Is.EqualTo(1));
			Assert.That(constraint.MaxWidgetColumnsDeviceName, Is.EqualTo("Client A"));
			Assert.That(constraint.RowsLocked || constraint.ColumnsLocked, Is.False);
			Assert.That(constraint.HasConflict, Is.False);
			Assert.That(constraint.ConstrainsGrid, Is.False);
			Assert.That((constraint.MaxRows, constraint.MaxColumns), Is.EqualTo((16, 16)));
			Assert.That(constraint.AllowsEdit(20, 20), Is.True);
			Assert.That(constraint.ExceedsLockedLayout(20, 20), Is.False);
		});
	}

	[Test]
	public void ALimitBelowOne_ReadsAsOne()
	{
		var constraint = DeviceLayoutConstraintResolver.Resolve([Device("Deck A", WithSpans(FixedGrid(3, 5), 0, -2))])!;

		Assert.Multiple(() =>
		{
			Assert.That(constraint.MaxWidgetColumns, Is.EqualTo(1));
			Assert.That(constraint.MaxWidgetRows, Is.EqualTo(1));
		});
	}

	[Test]
	public void AllowsWidgetSpan_RefusesANewOrGrowingWidgetPastTheLimit_ButKeepsAStoredLargerOneUsable()
	{
		var constraint
			= DeviceLayoutConstraintResolver.Resolve([Device("Stream Deck", WithSpans(FixedGrid(3, 5), 1, 1))])!;

		Assert.Multiple(() =>
		{
			Assert.That(constraint.AllowsWidgetSpan(1, 1, null, null), Is.True);
			Assert.That(constraint.AllowsWidgetSpan(2, 1, null, null), Is.False);
			Assert.That(constraint.AllowsWidgetSpan(2, 1, 1, 1), Is.False);
			Assert.That(constraint.AllowsWidgetSpan(1, 2, 1, 1), Is.False);
			Assert.That(constraint.AllowsWidgetSpan(3, 2, 3, 2), Is.True);
			Assert.That(constraint.AllowsWidgetSpan(2, 1, 3, 2), Is.True);
			Assert.That(constraint.AllowsWidgetSpan(4, 2, 3, 2), Is.False);
		});
	}

	private static DeviceEntity Device(string name, LayoutDescriptor layout)
	{
		return Device(name, LayoutSnapshotSerializer.Serialize(layout));
	}

	private static DeviceEntity Device(string name, string? layoutSnapshot)
	{
		return new DeviceEntity
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
	}

	private static LayoutDescriptor FixedGrid(int rows, int columns)
	{
		return new LayoutDescriptor("fixed",
			"Fixed Grid",
			[
				new LayoutRegion
				{
					Id = "grid", Kind = LayoutRegionKinds.Grid, Grid = new LayoutGrid { Rows = rows, Columns = columns }
				}
			]);
	}

	private static LayoutDescriptor WithSpans(LayoutDescriptor layout, int? maxWidgetColumns, int? maxWidgetRows)
	{
		return layout with
		{
			Regions =
			[
				layout.Regions[0] with
				{
					Grid = layout.Regions[0].Grid! with
					{
						MaxWidgetColumns = maxWidgetColumns, MaxWidgetRows = maxWidgetRows
					}
				}
			]
		};
	}

	private static LayoutDescriptor ConfigurableGrid(
		int rows,
		int columns,
		int minRows,
		int maxRows,
		int minColumns,
		int maxColumns)
	{
		return new LayoutDescriptor("configurable",
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
}
