using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Devices;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Persistence;
using MacroDeckHost.Infrastructure.Persistence.Repositories;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Tests.UnitTests.Triggers;
using Microsoft.Data.Sqlite;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Persistence;

[TestFixture]
[NonParallelizable]
public class DeviceSettingsButtonWriteTests
{
	private string _dataDir = null!;
	private string? _previousDataDir;
	private MacroDeckPaths _paths = null!;

	[SetUp]
	public void SetUp()
	{
		_dataDir = Path.Combine(Path.GetTempPath(), "macro-deck-tests", Guid.NewGuid().ToString("N"));
		_previousDataDir = Environment.GetEnvironmentVariable("MACRODECK_DATA_DIR");
		Environment.SetEnvironmentVariable("MACRODECK_DATA_DIR", _dataDir);

		_paths = new MacroDeckPaths();
		_paths.EnsureDirectoriesExist();
		DatabaseMigrationHelper.MigrateDatabase(_paths);
	}

	[TearDown]
	public void TearDown()
	{
		Environment.SetEnvironmentVariable("MACRODECK_DATA_DIR", _previousDataDir);
		SqliteConnection.ClearAllPools();

		if (Directory.Exists(_dataDir))
		{
			Directory.Delete(_dataDir, recursive: true);
		}
	}

	[Test]
	public async Task Hiding_the_button_from_a_long_lived_scope_keeps_edits_made_elsewhere_meanwhile()
	{
		var id = Guid.NewGuid();
		await using (var seed = new DatabaseContext(Log.Logger, _paths))
		{
			seed.Devices.Add(new DeviceEntity
			{
				Id = id,
				SecretHash = string.Empty,
				Name = "Old name",
				ClientType = DeviceClientType.WebClient,
				StartupProfileId = "profile-old"
			});
			await seed.SaveChangesAsync();
		}

		await using var contextA = new DatabaseContext(Log.Logger, _paths);
		var repositoryA = new DeviceRepository(contextA);
		Assert.That(await repositoryA.GetById(id), Is.Not.Null);

		await using (var contextB = new DatabaseContext(Log.Logger, _paths))
		{
			var repositoryB = new DeviceRepository(contextB);
			var device = (await repositoryB.GetById(id))!;
			device.Name = "New name";
			device.StartupProfileId = "profile-new";
			await repositoryB.Update(device);
		}

		var result = await ServiceOver(repositoryA).SetSettingsButtonHidden(id, true);

		await using var check = new DatabaseContext(Log.Logger, _paths);
		var stored = (await new DeviceRepository(check).GetById(id))!;
		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(result.Data!.SettingsButtonHidden, Is.True);
			Assert.That(stored.SettingsButtonHidden, Is.True);
			Assert.That(stored.Name, Is.EqualTo("New name"));
			Assert.That(stored.StartupProfileId, Is.EqualTo("profile-new"));
		});
	}

	[Test]
	public async Task A_later_save_in_the_long_lived_scope_does_not_bring_back_an_overridden_value()
	{
		var id = Guid.NewGuid();
		await using (var seed = new DatabaseContext(Log.Logger, _paths))
		{
			seed.Devices.Add(new DeviceEntity
			{
				Id = id, SecretHash = string.Empty, Name = "Deck", ClientType = DeviceClientType.WebClient
			});
			await seed.SaveChangesAsync();
		}

		await using var contextA = new DatabaseContext(Log.Logger, _paths);
		var repositoryA = new DeviceRepository(contextA);
		Assert.That(await repositoryA.GetById(id), Is.Not.Null);
		await ServiceOver(repositoryA).SetSettingsButtonHidden(id, true);

		await using (var contextB = new DatabaseContext(Log.Logger, _paths))
		{
			await new DeviceRepository(contextB).SetSettingsButtonHidden(id, false);
		}

		await contextA.SaveChangesAsync();

		await using var check = new DatabaseContext(Log.Logger, _paths);
		Assert.That((await new DeviceRepository(check).GetById(id))!.SettingsButtonHidden, Is.False);
	}

	[Test]
	public async Task Hiding_the_button_of_a_missing_device_reports_that_no_row_matched()
	{
		await using var context = new DatabaseContext(Log.Logger, _paths);

		var matched = await new DeviceRepository(context).SetSettingsButtonHidden(Guid.NewGuid(), true);

		Assert.That(matched, Is.False);
	}

	private static DeviceService ServiceOver(DeviceRepository repository)
	{
		var time = new ManualTimeProvider();
		var readiness = new StartupReadiness();
		readiness.MarkCachesReady();
		readiness.MarkVariablesReady();
		return new DeviceService(repository,
			new InMemoryRefreshTokenRepository(),
			new DeviceConnectionTracker(new RecordingEventBus(),
				time,
				new MacroDeckHost.Application.Deck.DeckClientTracker(Serilog.Core.Logger.None)),
			new RecordingUiTransport(),
			new RecordingMediator(),
			time,
			new FakeProfileRegistry(),
			new FakeDeviceDeckNavigator(),
			readiness,
			new ProviderDevicePresenceTracker(),
			new FakeIntegrationRegistry(),
			TestScreenSaverProviders.Registry(),
			new DeviceSessionGuard());
	}
}
