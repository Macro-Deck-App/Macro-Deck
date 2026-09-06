using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Logging;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Tests.UnitTests.Services;

public class AppPreferenceServiceTests
{
	private sealed class FakeAppPreferenceRepository : IAppPreferenceRepository
	{
		private readonly Dictionary<string, AppPreferenceEntity> _store = new();

		public Task<AppPreferenceEntity?> GetByKey(string key)
			=> Task.FromResult(_store.GetValueOrDefault(key));

		public Task SetValue(string key, string value)
		{
			_store[key] = new AppPreferenceEntity { Key = key, Value = value };
			return Task.CompletedTask;
		}
	}

	private sealed class FakeBuildEnvironment : IBuildEnvironment
	{
		public string Version => "0.0.0-test";

		public bool IsBeta { get; init; }

		public BuildChannel Channel { get; init; } = BuildChannel.Production;
	}

	private static AppPreferenceService CreateService(
		bool isBeta = false,
		BuildChannel channel = BuildChannel.Production)
		=> CreateService(new FakeAppPreferenceRepository(), isBeta, channel);

	private static AppPreferenceService CreateService(
		IAppPreferenceRepository repository,
		bool isBeta = false,
		BuildChannel channel = BuildChannel.Production)
		=> new(repository,
			new FakeBuildEnvironment { IsBeta = isBeta, Channel = channel },
			new FakeHostListenerState());

	[Test]
	public async Task GetOnboarding_is_not_pending_when_unset()
	{
		var service = CreateService();

		var settings = await service.GetOnboarding();

		Assert.That(settings.Pending, Is.False);
	}

	[Test]
	public async Task SetOnboarding_round_trips_through_the_store()
	{
		var repository = new FakeAppPreferenceRepository();
		var service = CreateService(repository);

		var armed = await service.SetOnboarding(true);
		var reloaded = await CreateService(repository).GetOnboarding();

		Assert.Multiple(() =>
		{
			Assert.That(armed.Pending, Is.True);
			Assert.That(reloaded.Pending, Is.True);
		});
	}

	[Test]
	public async Task SetOnboarding_false_survives_a_restart()
	{
		var repository = new FakeAppPreferenceRepository();
		await CreateService(repository).SetOnboarding(true);

		var completed = await CreateService(repository).SetOnboarding(false);
		var reloaded = await CreateService(repository).GetOnboarding();

		Assert.Multiple(() =>
		{
			Assert.That(completed.Pending, Is.False);
			Assert.That(reloaded.Pending, Is.False);
		});
	}

	[Test]
	public async Task GetOnboarding_treats_an_unreadable_value_as_not_pending()
	{
		var repository = new FakeAppPreferenceRepository();
		await repository.SetValue(AppPreferenceService.OnboardingPendingKey, "maybe");

		var settings = await CreateService(repository).GetOnboarding();

		Assert.That(settings.Pending, Is.False);
	}

	[Test]
	public async Task GetAppearance_returns_defaults_when_unset()
	{
		var service = CreateService();

		var settings = await service.GetAppearance();

		Assert.Multiple(() =>
		{
			Assert.That(settings.ThemeMode, Is.EqualTo("system"));
			Assert.That(settings.AccentColor, Is.EqualTo("#2196F3"));
		});
	}

	[Test]
	public async Task SetAppearance_persists_valid_values()
	{
		var service = CreateService();

		var applied = await service.SetAppearance("dark", "#ff0000");
		var reloaded = await service.GetAppearance();

		Assert.Multiple(() =>
		{
			Assert.That(applied.ThemeMode, Is.EqualTo("dark"));
			Assert.That(applied.AccentColor, Is.EqualTo("#ff0000"));
			Assert.That(reloaded.ThemeMode, Is.EqualTo("dark"));
			Assert.That(reloaded.AccentColor, Is.EqualTo("#ff0000"));
		});
	}

	[Test]
	public async Task SetAppearance_accepts_shorthand_hex()
	{
		var service = CreateService();

		var applied = await service.SetAppearance("light", "#abc");

		Assert.That(applied.AccentColor, Is.EqualTo("#abc"));
	}

	[Test]
	public async Task SetAppearance_falls_back_to_defaults_for_invalid_input()
	{
		var service = CreateService();

		var applied = await service.SetAppearance("rainbow", "not-a-color");

		Assert.Multiple(() =>
		{
			Assert.That(applied.ThemeMode, Is.EqualTo("system"));
			Assert.That(applied.AccentColor, Is.EqualTo("#2196F3"));
		});
	}

	[Test]
	public async Task SetAppearance_treats_null_input_as_defaults()
	{
		var service = CreateService();

		var applied = await service.SetAppearance(null, null);

		Assert.Multiple(() =>
		{
			Assert.That(applied.ThemeMode, Is.EqualTo("system"));
			Assert.That(applied.AccentColor, Is.EqualTo("#2196F3"));
		});
	}

	[Test]
	public async Task GetInstallationId_creates_id_on_first_access_and_keeps_it_stable()
	{
		var service = CreateService();

		var first = await service.GetInstallationId();
		var second = await service.GetInstallationId();

		Assert.Multiple(() =>
		{
			Assert.That(first, Is.Not.EqualTo(Guid.Empty));
			Assert.That(second, Is.EqualTo(first));
		});
	}

	[Test]
	public async Task GetInstallationId_replaces_invalid_stored_value()
	{
		var repository = new FakeAppPreferenceRepository();
		await repository.SetValue(AppPreferenceService.InstallationIdKey, "not-a-guid");
		var service = new AppPreferenceService(repository, new FakeBuildEnvironment(), new FakeHostListenerState());

		var id = await service.GetInstallationId();
		var reloaded = await service.GetInstallationId();

		Assert.Multiple(() =>
		{
			Assert.That(id, Is.Not.EqualTo(Guid.Empty));
			Assert.That(reloaded, Is.EqualTo(id));
		});
	}

	[Test]
	public async Task GetLogging_defaults_to_Debug_on_development_builds()
	{
		var service = CreateService(channel: BuildChannel.Development);

		var settings = await service.GetLogging();

		Assert.Multiple(() =>
		{
			Assert.That(settings.MinimumLevel, Is.EqualTo(LogEntryLevel.Debug));
			Assert.That(settings.DefaultMinimumLevel, Is.EqualTo(LogEntryLevel.Debug));
		});
	}

	[Test]
	public async Task GetLogging_defaults_to_Information_on_production_builds()
	{
		var service = CreateService(channel: BuildChannel.Production);

		var settings = await service.GetLogging();

		Assert.Multiple(() =>
		{
			Assert.That(settings.MinimumLevel, Is.EqualTo(LogEntryLevel.Information));
			Assert.That(settings.DefaultMinimumLevel, Is.EqualTo(LogEntryLevel.Information));
		});
	}

	[Test]
	public async Task GetLogging_defaults_to_Information_on_beta_builds()
	{
		var service = CreateService(isBeta: true, channel: BuildChannel.Production);

		var settings = await service.GetLogging();

		Assert.That(settings.MinimumLevel, Is.EqualTo(LogEntryLevel.Information));
	}

	[Test]
	public async Task SetLogging_persists_the_requested_level()
	{
		var service = CreateService();

		var applied = await service.SetLogging(LogEntryLevel.Warning);
		var reloaded = await service.GetLogging();

		Assert.Multiple(() =>
		{
			Assert.That(applied.MinimumLevel, Is.EqualTo(LogEntryLevel.Warning));
			Assert.That(reloaded.MinimumLevel, Is.EqualTo(LogEntryLevel.Warning));
			Assert.That(reloaded.DefaultMinimumLevel, Is.EqualTo(LogEntryLevel.Information));
		});
	}

	[Test]
	public async Task SetLogging_without_a_level_resets_to_the_channel_default()
	{
		var service = CreateService(channel: BuildChannel.Development);
		await service.SetLogging(LogEntryLevel.Error);

		var applied = await service.SetLogging(null);
		var reloaded = await service.GetLogging();

		Assert.Multiple(() =>
		{
			Assert.That(applied.MinimumLevel, Is.EqualTo(LogEntryLevel.Debug));
			Assert.That(reloaded.MinimumLevel, Is.EqualTo(LogEntryLevel.Debug));
		});
	}

	[TestCase("nonsense")]
	[TestCase("9")]
	[TestCase("")]
	public async Task GetLogging_falls_back_to_the_default_for_an_unusable_stored_value(string stored)
	{
		var repository = new FakeAppPreferenceRepository();
		await repository.SetValue(AppPreferenceService.MinimumLogLevelKey, stored);
		var service = new AppPreferenceService(repository, new FakeBuildEnvironment(), new FakeHostListenerState());

		var settings = await service.GetLogging();

		Assert.That(settings.MinimumLevel, Is.EqualTo(LogEntryLevel.Information));
	}

	[Test]
	public async Task GetLogging_accepts_a_stored_value_regardless_of_casing()
	{
		var repository = new FakeAppPreferenceRepository();
		await repository.SetValue(AppPreferenceService.MinimumLogLevelKey, "verbose");
		var service = new AppPreferenceService(repository, new FakeBuildEnvironment(), new FakeHostListenerState());

		var settings = await service.GetLogging();

		Assert.That(settings.MinimumLevel, Is.EqualTo(LogEntryLevel.Verbose));
	}

	[Test]
	public async Task GetAdb_returns_disabled_defaults_when_unset()
	{
		var service = CreateService();

		var settings = await service.GetAdb();

		Assert.Multiple(() =>
		{
			Assert.That(settings.Enabled, Is.False);
			Assert.That(settings.ExecutablePath, Is.Null);
			Assert.That(settings.UsbConnectionsEnabled, Is.True);
			Assert.That(settings.DefaultDeviceSerial, Is.Null);
		});
	}

	[Test]
	public async Task SetAdb_persists_valid_values()
	{
		var service = CreateService();

		var applied = await service.SetAdb(true, "/usr/local/bin/adb", true, "R58M12ABCDE");
		var reloaded = await service.GetAdb();

		Assert.Multiple(() =>
		{
			Assert.That(applied.Enabled, Is.True);
			Assert.That(applied.ExecutablePath, Is.EqualTo("/usr/local/bin/adb"));
			Assert.That(applied.UsbConnectionsEnabled, Is.True);
			Assert.That(applied.DefaultDeviceSerial, Is.EqualTo("R58M12ABCDE"));
			Assert.That(reloaded.Enabled, Is.True);
			Assert.That(reloaded.ExecutablePath, Is.EqualTo("/usr/local/bin/adb"));
			Assert.That(reloaded.UsbConnectionsEnabled, Is.True);
			Assert.That(reloaded.DefaultDeviceSerial, Is.EqualTo("R58M12ABCDE"));
		});
	}

	[Test]
	public async Task SetAdb_falls_back_to_each_flags_own_default_and_treats_null_strings_as_unset()
	{
		var service = CreateService();
		await service.SetAdb(true, "/usr/local/bin/adb", false, "R58M12ABCDE");

		var applied = await service.SetAdb(null, null, null, null);

		Assert.Multiple(() =>
		{
			Assert.That(applied.Enabled, Is.False);
			Assert.That(applied.ExecutablePath, Is.Null);
			Assert.That(applied.UsbConnectionsEnabled, Is.True);
			Assert.That(applied.DefaultDeviceSerial, Is.Null);
		});
	}

	[TestCase("  /usr/local/bin/adb  ", "/usr/local/bin/adb")]
	[TestCase("   ", null)]
	[TestCase("", null)]
	public async Task SetAdb_trims_the_executable_path_and_blanks_out_to_null(string? input, string? expected)
	{
		var service = CreateService();

		var applied = await service.SetAdb(true, input, false, null);

		Assert.That(applied.ExecutablePath, Is.EqualTo(expected));
	}

	[Test]
	public async Task SetAdb_accepts_a_well_formed_default_device_serial()
	{
		var service = CreateService();

		var applied = await service.SetAdb(true, null, true, "  R58M12ABCDE  ");

		Assert.That(applied.DefaultDeviceSerial, Is.EqualTo("R58M12ABCDE"));
	}

	[Test]
	public async Task SetAdb_rejects_a_default_device_serial_longer_than_128_characters()
	{
		var service = CreateService();
		var tooLong = new string('a', 129);

		var applied = await service.SetAdb(true, null, true, tooLong);

		Assert.That(applied.DefaultDeviceSerial, Is.Null);
	}

	[TestCase("R58M12ABCDE")]
	[TestCase("emulator-5554")]
	[TestCase("192.168.1.5:5555")]
	public async Task SetAdb_accepts_every_character_the_serial_pattern_allows(string serial)
	{
		var service = CreateService();

		var applied = await service.SetAdb(true, null, true, serial);

		Assert.That(applied.DefaultDeviceSerial, Is.EqualTo(serial));
	}

	[TestCase("has a space")]
	[TestCase("semi;colon")]
	[TestCase("quote\"mark")]
	public async Task SetAdb_rejects_a_default_device_serial_with_characters_outside_the_allowed_pattern(string serial)
	{
		var service = CreateService();

		var applied = await service.SetAdb(true, null, true, serial);

		Assert.That(applied.DefaultDeviceSerial, Is.Null);
	}

	[Test]
	public async Task GetAdb_treats_an_unparseable_stored_flag_as_false()
	{
		var repository = new FakeAppPreferenceRepository();
		await repository.SetValue(AppPreferenceService.AdbEnabledKey, "maybe");
		var service = new AppPreferenceService(repository, new FakeBuildEnvironment(), new FakeHostListenerState());

		var settings = await service.GetAdb();

		Assert.That(settings.Enabled, Is.False);
	}

	[Test]
	public async Task GetAdb_treats_a_stored_serial_that_no_longer_matches_the_pattern_as_unset()
	{
		var repository = new FakeAppPreferenceRepository();
		await repository.SetValue(AppPreferenceService.AdbDefaultDeviceSerialKey, "not a valid serial!");
		var service = new AppPreferenceService(repository, new FakeBuildEnvironment(), new FakeHostListenerState());

		var settings = await service.GetAdb();

		Assert.That(settings.DefaultDeviceSerial, Is.Null);
	}

	[Test]
	public async Task GetLockScreen_defaults_to_disabled_when_unset()
	{
		var service = CreateService();

		var settings = await service.GetLockScreen();

		Assert.That(settings.Enabled, Is.False);
	}

	[Test]
	public async Task SetLockScreen_persists_the_requested_value()
	{
		var service = CreateService();

		var applied = await service.SetLockScreen(true);
		var reloaded = await service.GetLockScreen();

		Assert.Multiple(() =>
		{
			Assert.That(applied.Enabled, Is.True);
			Assert.That(reloaded.Enabled, Is.True);
		});
	}

	[Test]
	public async Task SetLockScreen_treats_null_input_as_disabled()
	{
		var service = CreateService();
		await service.SetLockScreen(true);

		var applied = await service.SetLockScreen(null);

		Assert.That(applied.Enabled, Is.False);
	}

	[Test]
	public async Task GetLockScreen_treats_an_unparseable_stored_value_as_disabled()
	{
		var repository = new FakeAppPreferenceRepository();
		await repository.SetValue(AppPreferenceService.LockScreenEnabledKey, "maybe");
		var service = new AppPreferenceService(repository, new FakeBuildEnvironment(), new FakeHostListenerState());

		var settings = await service.GetLockScreen();

		Assert.That(settings.Enabled, Is.False);
	}

	[Test]
	public async Task GetExtensions_defaults_to_an_hourly_refresh_with_everything_enabled()
	{
		var service = CreateService();

		var settings = await service.GetExtensions();

		Assert.Multiple(() =>
		{
			Assert.That(settings.StoreEnabled, Is.True);
			Assert.That(settings.CheckForUpdates, Is.True);
			Assert.That(settings.NotifyOnUpdates, Is.True);
			Assert.That(settings.RefreshIntervalMinutes, Is.EqualTo(60));
		});
	}

	[TestCase(1, 15)]
	[TestCase(14, 15)]
	[TestCase(15, 15)]
	[TestCase(60, 60)]
	[TestCase(61, 60)]
	[TestCase(1440, 60)]
	public async Task SetExtensions_clamps_the_refresh_interval_to_15_60_minutes(int requested, int expected)
	{
		var service = CreateService();

		var applied = await service.SetExtensions(null, null, null, requested);
		var reloaded = await service.GetExtensions();

		Assert.Multiple(() =>
		{
			Assert.That(applied.RefreshIntervalMinutes, Is.EqualTo(expected));
			Assert.That(reloaded.RefreshIntervalMinutes, Is.EqualTo(expected));
		});
	}

	[Test]
	public async Task GetExtensions_treats_an_unparseable_stored_interval_as_the_hourly_default()
	{
		var repository = new FakeAppPreferenceRepository();
		await repository.SetValue(AppPreferenceService.ExtensionsRefreshIntervalMinutesKey, "not a number");
		var service = new AppPreferenceService(repository, new FakeBuildEnvironment(), new FakeHostListenerState());

		var settings = await service.GetExtensions();

		Assert.That(settings.RefreshIntervalMinutes, Is.EqualTo(60));
	}
}
