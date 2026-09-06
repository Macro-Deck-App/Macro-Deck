using MacroDeck.Sdk.Layouts;
using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Layouts;

/// <summary>
/// The tracker side of issue #384: it snapshots what <see cref="DeviceLayoutConstraintResolver" />
/// would say per profile, so the profile DTO mapper can read it synchronously off the request path.
/// </summary>
public class DeviceLayoutConstraintTrackerTests
{
	[Test]
	public void BeforeAnyRefresh_GetReturnsNull_AndNeverThrows()
	{
		var tracker = TestDeviceLayoutConstraintTracker.Build();

		Assert.That(tracker.Get(Guid.NewGuid().ToString()), Is.Null);
	}

	[Test]
	public async Task AfterRefresh_GetReturnsTheResolvedConstraint_ForAClaimedProfile()
	{
		var profileId = Guid.NewGuid().ToString();
		var devices = new InMemoryDeviceRepository();
		devices.Devices.Add(Device("Deck A", profileId, FixedGrid(5, 3)));
		var tracker = TestDeviceLayoutConstraintTracker.Build(devices);

		await tracker.RefreshAsync();

		var constraint = tracker.Get(profileId);

		Assert.Multiple(() =>
		{
			Assert.That(constraint, Is.Not.Null);
			Assert.That(constraint!.RowsLocked, Is.True);
			Assert.That(constraint.ColumnsLocked, Is.True);
			Assert.That(constraint.Rows, Is.EqualTo(5));
			Assert.That(constraint.Columns, Is.EqualTo(3));
			Assert.That(constraint.DeviceName, Is.EqualTo("Deck A"));
		});
	}

	[Test]
	public async Task AfterRefresh_GetReturnsNull_ForAProfileNoDeviceClaims()
	{
		var devices = new InMemoryDeviceRepository();
		devices.Devices.Add(Device("Deck A", Guid.NewGuid().ToString(), FixedGrid(5, 3)));
		var tracker = TestDeviceLayoutConstraintTracker.Build(devices);

		await tracker.RefreshAsync();

		Assert.That(tracker.Get(Guid.NewGuid().ToString()), Is.Null);
	}

	[Test]
	public async Task ARefresh_DropsAProfileThatNoLongerHasAClaimingDevice()
	{
		var profileId = Guid.NewGuid().ToString();
		var devices = new InMemoryDeviceRepository();
		var device = Device("Deck A", profileId, FixedGrid(5, 3));
		devices.Devices.Add(device);
		var tracker = TestDeviceLayoutConstraintTracker.Build(devices);
		await tracker.RefreshAsync();
		Assert.That(tracker.Get(profileId), Is.Not.Null);

		devices.Devices.Remove(device);
		await tracker.RefreshAsync();

		Assert.That(tracker.Get(profileId), Is.Null);
	}

	private static DeviceEntity Device(string name, string startupProfileId, LayoutDescriptor layout)
		=> new()
		{
			Id = Guid.NewGuid(),
			SecretHash = string.Empty,
			Name = name,
			ClientType = DeviceClientType.Provider,
			FormFactor = DeviceFormFactor.Unknown,
			StartupProfileId = startupProfileId,
			LayoutReference = "owner::layout",
			LayoutSnapshot = LayoutSnapshotSerializer.Serialize(layout),
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
}
