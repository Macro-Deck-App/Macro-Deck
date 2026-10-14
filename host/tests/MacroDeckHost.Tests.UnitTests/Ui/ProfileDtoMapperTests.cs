using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Tests.UnitTests.Ui;

/// <summary>
/// The profile-DTO side of issue #384: a device grid constraint reaches the wire as
/// <c>ProfileLayout.Constraint</c>/<c>Compatibility</c>, without ever touching the read-only
/// <c>RowsLocked</c>/<c>ColumnsLocked</c> flags a real (non-virtual) profile always reports as false.
/// </summary>
[TestFixture]
public class ProfileDtoMapperTests
{
	[Test]
	public void NoConstraint_ProducesNoConstraintOrCompatibility()
	{
		var dto = ProfileDtoMapper.MapJsonProfile(Profile(4, 4));

		Assert.Multiple(() =>
		{
			Assert.That(dto.Layout.Constraint, Is.Null);
			Assert.That(dto.Layout.Compatibility, Is.Null);
		});
	}

	[TestCase(null)]
	[TestCase(false)]
	[TestCase(true)]
	public void TheProfileWidgetShadowsSettingReachesTheWire(bool? shadows)
	{
		var entity = Profile(4, 4);
		entity.DefaultWidgetShadows = shadows;

		var dto = ProfileDtoMapper.MapJsonProfile(entity);

		Assert.That(dto.DefaultWidgetShadows, Is.EqualTo(shadows));
	}

	[Test]
	public void ACurrentGridLargerThanALockedLayout_IsReportedAsExceedsLayout_WithoutResizingTheDto()
	{
		var entity = Profile(6, 6);
		var constraint = FixedConstraint(4, 4, "Deck A");

		var dto = ProfileDtoMapper.MapJsonProfile(entity, constraint);

		Assert.Multiple(() =>
		{
			Assert.That(dto.Layout.Compatibility, Is.Not.Null);
			Assert.That(dto.Layout.Compatibility!.Status, Is.EqualTo("exceedsLayout"));
			Assert.That(dto.Layout.Rows, Is.EqualTo(6));
			Assert.That(dto.Layout.Columns, Is.EqualTo(6));
			Assert.That(dto.DefaultRows, Is.EqualTo(6));
			Assert.That(dto.DefaultColumns, Is.EqualTo(6));
		});
	}

	[Test]
	public void AConflictingConstraint_IsReportedAsConflictingDevices_NamingEveryConflictingDevice()
	{
		var entity = Profile(4, 4);
		var constraint = ConflictConstraint(["Deck A", "Deck B", "Deck C"]);

		var dto = ProfileDtoMapper.MapJsonProfile(entity, constraint);

		Assert.Multiple(() =>
		{
			Assert.That(dto.Layout.Compatibility, Is.Not.Null);
			Assert.That(dto.Layout.Compatibility!.Status, Is.EqualTo("conflictingDevices"));
			Assert.That(dto.Layout.Compatibility.DeviceNames, Is.EquivalentTo(["Deck A", "Deck B", "Deck C"]));
		});
	}

	[Test]
	public void ARealProfileWithADeviceConstraint_StillReportsUnlockedTopLevelFlags()
	{
		var entity = Profile(4, 4);
		var constraint = FixedConstraint(4, 4, "Deck A");

		var dto = ProfileDtoMapper.MapJsonProfile(entity, constraint);

		// The regression this guards against: RowsLocked/ColumnsLocked mean "integration-owned virtual
		// profile, read-only deck" everywhere else in the wire format (ProfileRegistry.MapVirtualProfile,
		// the Angular client). A device grid constraint on a real profile must never set them, or an
		// otherwise-editable profile with a deck assigned would turn read-only.
		Assert.Multiple(() =>
		{
			Assert.That(dto.Layout.RowsLocked, Is.False);
			Assert.That(dto.Layout.ColumnsLocked, Is.False);
		});
	}

	[Test]
	public void TheNoConstraintOverload_StillProducesNoConstraintOrCompatibility()
	{
		var dto = ProfileDtoMapper.MapJsonProfile(Profile(4, 4));

		Assert.Multiple(() =>
		{
			Assert.That(dto.Layout.Constraint, Is.Null);
			Assert.That(dto.Layout.Compatibility, Is.Null);
			Assert.That(dto.Layout.RowsLocked, Is.False);
			Assert.That(dto.Layout.ColumnsLocked, Is.False);
		});
	}

	private static ProfileEntity Profile(int rows, int columns)
	{
		return new ProfileEntity
		{
			Id = Guid.NewGuid(),
			Name = "Profile",
			LayoutType = ProfileLayoutType.Grid,
			DefaultRows = rows,
			DefaultColumns = columns
		};
	}

	private static DeviceGridConstraint FixedConstraint(int rows, int columns, string deviceName)
	{
		return new DeviceGridConstraint(true,
			true,
			rows,
			columns,
			rows,
			rows,
			columns,
			columns,
			"owner::layout",
			"Fixed Grid",
			deviceName,
			false,
			[]);
	}

	private static DeviceGridConstraint ConflictConstraint(IReadOnlyList<string> deviceNames)
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
			true,
			deviceNames);
	}
}
