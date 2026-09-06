using MacroDeck.Sdk.Layouts;
using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Services;

/// <summary>A child folder cannot override its way past a fixed device layout (issue #384): the check
/// runs on the folder's own effective, inherited grid.</summary>
[TestFixture]
public class FolderServiceDeviceLockTests
{
	private ProfileCache _cache = null!;
	private FolderCache _folderCache = null!;
	private InMemoryDeviceRepository _devices = null!;
	private FolderService _folderService = null!;
	private ProfileService _profileService = null!;
	private Guid _profileId;

	[SetUp]
	public async Task SetUp()
	{
		_cache = new ProfileCache(new InMemoryProfileStore(), new LoggerConfiguration().CreateLogger());
		await _cache.InitializeCache();
		_folderCache = new FolderCache(_cache);
		_devices = new InMemoryDeviceRepository();
		var secrets = new FakeSecretService();
		_folderService = new FolderService(_folderCache,
			_cache,
			_devices,
			new RecordingMediator(),
			new WidgetSecretCloner(secrets),
			new WidgetSecretScrubber(secrets),
			new NullWidgetVariableCloner(),
			TestFolderViewProviders.Registry());
		_profileService = new ProfileService(_cache, _folderCache, _devices, new RecordingMediator());

		var created = await _profileService.Create("Deck Profile", defaultRows: 4, defaultColumns: 4);
		_profileId = created.Data!.Id;

		Claim(FixedGrid(5, 3), "Deck A");
	}

	[TearDown]
	public void TearDown() => _cache.Dispose();

	[Test]
	public async Task AChildFolder_CannotOverrideItsOwnGrid_PastTheDevicesFixedLayout()
	{
		var child = (await _folderService.Create(_profileId, "Games", null)).Data!;

		var result = await _folderService.Update(child.Id, null, null, null, 6, 6, null, null, null, null);

		Assert.That(result.Success, Is.False);
		Assert.That(result.Error, Is.EqualTo(FolderError.GridLockedByDevice));
	}

	[Test]
	public async Task AChildFolder_MayStillSetItsGridToExactlyTheLockedSize()
	{
		var child = (await _folderService.Create(_profileId, "Games", null)).Data!;

		var result = await _folderService.Update(child.Id, null, null, null, 5, 3, null, null, null, null);

		Assert.That(result.Success, Is.True);
	}

	[Test]
	public async Task EditingSomethingOtherThanTheGrid_IsUnaffectedByTheLock()
	{
		var child = (await _folderService.Create(_profileId, "Games", null)).Data!;

		var result = await _folderService.Update(child.Id,
			"Renamed",
			null,
			null,
			null,
			null,
			"#ffffff",
			null,
			null,
			null);

		Assert.That(result.Success, Is.True);
	}

	private void Claim(LayoutDescriptor layout, string name)
	{
		_devices.Devices.Add(new DeviceEntity
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
		});
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
