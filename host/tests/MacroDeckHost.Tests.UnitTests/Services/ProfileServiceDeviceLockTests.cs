using MacroDeck.Sdk.Layouts;
using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Services;

/// <summary>
/// The device-claims-a-profile side of issue #384: a device's startup profile assignment, and its
/// persisted layout snapshot, constrain what <c>ProfileService.Update</c> accepts.
/// </summary>
[TestFixture]
public class ProfileServiceDeviceLockTests
{
	private ProfileCache _cache = null!;
	private InMemoryDeviceRepository _devices = null!;
	private ProfileService _service = null!;
	private Guid _profileId;

	[SetUp]
	public async Task SetUp()
	{
		_cache = new ProfileCache(new InMemoryProfileStore(), new LoggerConfiguration().CreateLogger());
		await _cache.InitializeCache();
		_devices = new InMemoryDeviceRepository();
		_service = new ProfileService(_cache, new FolderCache(_cache), _devices, new RecordingMediator());

		var created = await _service.Create("Deck Profile", defaultRows: 4, defaultColumns: 4);
		_profileId = created.Data!.Id;
	}

	[TearDown]
	public void TearDown() => _cache.Dispose();

	[Test]
	public async Task OneClaimingFixedDevice_LocksTheProfileToItsGrid()
	{
		Claim(FixedGrid(5, 3), "Deck A");

		var result = await _service.Update(_profileId, null, null, 5, 3, null, null, null);
		Assert.That(result.Success, Is.True);

		var blocked = await _service.Update(_profileId, null, null, 6, 3, null, null, null);

		Assert.Multiple(() =>
		{
			Assert.That(blocked.Success, Is.False);
			Assert.That(blocked.Error, Is.EqualTo(ProfileError.GridLockedByDevice));
			Assert.That(_cache.GetById(_profileId)!.DefaultRows, Is.EqualTo(5));
		});
	}

	[Test]
	public async Task TwoDevicesWithDifferentFixedGrids_DoNotBlockAResize()
	{
		Claim(FixedGrid(5, 3), "Deck A");
		Claim(FixedGrid(4, 8), "Deck B");

		var result = await _service.Update(_profileId, null, null, 6, 6, null, null, null);

		Assert.That(result.Success, Is.True);
	}

	[Test]
	public async Task AStoppedProvider_StillConstrainsThroughThePersistedSnapshot()
	{
		// No ILayoutRegistry is consulted here at all - only the device's own persisted snapshot,
		// exactly as it would be after its provider has stopped.
		Claim(FixedGrid(5, 3), "Deck A");

		var blocked = await _service.Update(_profileId, null, null, 6, 3, null, null, null);

		Assert.That(blocked.Success, Is.False);
		Assert.That(blocked.Error, Is.EqualTo(ProfileError.GridLockedByDevice));
	}

	[Test]
	public async Task ADeletedDevice_ReleasesTheLock()
	{
		var device = Claim(FixedGrid(5, 3), "Deck A");
		await _devices.Delete(device.Id);

		var result = await _service.Update(_profileId, null, null, 6, 6, null, null, null);

		Assert.That(result.Success, Is.True);
	}

	[Test]
	public async Task AnUnresolvableLayoutReference_LeavesTheProfileFullyEditable()
	{
		var device = new DeviceEntity
		{
			Id = Guid.NewGuid(),
			SecretHash = string.Empty,
			Name = "Deck A",
			ClientType = DeviceClientType.Provider,
			FormFactor = DeviceFormFactor.Unknown,
			LayoutReference = "com.example.deck::missing",
			LayoutSnapshot = null,
			StartupProfileId = _profileId.ToString(),
			CreatedAt = DateTime.UtcNow,
			LastSeenAt = DateTime.UtcNow
		};
		_devices.Devices.Add(device);

		var result = await _service.Update(_profileId, null, null, 6, 6, null, null, null);

		Assert.That(result.Success, Is.True);
	}

	[Test]
	public async Task AProfileNoDeviceClaims_StaysFullyEditable()
	{
		var result = await _service.Update(_profileId, null, null, 9, 1, null, null, null);

		Assert.That(result.Success, Is.True);
	}

	[Test]
	public async Task GridTooSmallAndGridLockedByDevice_BothStillFire_InTheirOwnCases()
	{
		var folder = _cache.GetFoldersByProfileId(_profileId).Single();
		_cache.AddWidget(folder.Id,
			new WidgetEntity
			{
				Id = Guid.NewGuid(), Type = WidgetTypeIds.ActionButton, PositionX = 0, PositionY = 2, Width = 1,
				Height = 1
			});

		// GridTooSmall: unrelated to any device, a widget would fall outside the new default grid.
		var tooSmall = await _service.Update(_profileId, null, null, 2, null, null, null, null);
		Assert.That(tooSmall.Error, Is.EqualTo(ProfileError.GridTooSmall));

		Claim(FixedGrid(5, 4), "Deck A");

		// GridLockedByDevice: large enough for the widget, but not the size the claiming device locks to.
		var lockedElsewhere = await _service.Update(_profileId, null, null, 4, 4, null, null, null);
		Assert.That(lockedElsewhere.Error, Is.EqualTo(ProfileError.GridLockedByDevice));
	}

	private DeviceEntity Claim(LayoutDescriptor layout, string name)
	{
		var device = new DeviceEntity
		{
			Id = Guid.NewGuid(),
			SecretHash = string.Empty,
			Name = name,
			ClientType = DeviceClientType.Provider,
			FormFactor = DeviceFormFactor.Unknown,
			LayoutReference = "com.example.deck::grid",
			LayoutSnapshot = LayoutSnapshotSerializer.Serialize(layout),
			StartupProfileId = _profileId.ToString(),
			CreatedAt = DateTime.UtcNow,
			LastSeenAt = DateTime.UtcNow
		};
		_devices.Devices.Add(device);
		return device;
	}

	private static LayoutDescriptor FixedGrid(int rows, int columns)
		=> new("grid",
			"Deck",
			[
				new LayoutRegion
				{
					Id = "grid", Kind = LayoutRegionKinds.Grid, Grid = new LayoutGrid { Rows = rows, Columns = columns }
				}
			]);
}
