using System.Reflection;
using MacroDeckHost.Application.Adb;
using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Tests.UnitTests.Adb;

public class AdbSettingsHandlersTests
{
	private const int ActivePort = 6112;

	private sealed class FakeAppPreferenceRepository : IAppPreferenceRepository
	{
		private readonly Dictionary<string, AppPreferenceEntity> _store = new();

		public int Writes { get; private set; }

		public Task<AppPreferenceEntity?> GetByKey(string key)
			=> Task.FromResult(_store.GetValueOrDefault(key));

		public Task SetValue(string key, string value)
		{
			Writes++;
			_store[key] = new AppPreferenceEntity { Key = key, Value = value };
			return Task.CompletedTask;
		}

		public void Seed(string key, string value)
			=> _store[key] = new AppPreferenceEntity { Key = key, Value = value };
	}

	private sealed class FakeBuildEnvironment : IBuildEnvironment
	{
		public string Version => "0.0.0-test";

		public bool IsBeta => false;

		public BuildChannel Channel => BuildChannel.Production;
	}

	private sealed record Fixture(
		FakeAppPreferenceRepository Repository,
		FakeAdbManager AdbManager,
		FakeAdbPlatformToolsInstaller Installer,
		GetAdbSettingsRequestMessageHandler Get,
		UpdateAdbSettingsRequestMessageHandler Update,
		RestartAdbServerRequestMessageHandler Restart,
		DownloadAdbPlatformToolsRequestMessageHandler Download);

	private static Fixture CreateFixture()
	{
		var repository = new FakeAppPreferenceRepository();
		var listenerState = new FakeHostListenerState { PublicPort = ActivePort };
		var preferences = new AppPreferenceService(repository, new FakeBuildEnvironment(), listenerState);
		var adbManager = new FakeAdbManager();
		var installer = new FakeAdbPlatformToolsInstaller();

		return new Fixture(repository,
			adbManager,
			installer,
			new GetAdbSettingsRequestMessageHandler(preferences, adbManager, listenerState),
			new UpdateAdbSettingsRequestMessageHandler(preferences, adbManager, listenerState),
			new RestartAdbServerRequestMessageHandler(preferences, adbManager, listenerState),
			new DownloadAdbPlatformToolsRequestMessageHandler(preferences, adbManager, installer, listenerState));
	}

	private static AdbStatus SampleStatus(bool enabled = true,
		string? resolvedExecutablePath = "/usr/local/bin/adb",
		AdbExecutableSource executableSource = AdbExecutableSource.Configured,
		string? adbVersion = "1.0.41",
		bool serverReachable = true,
		string? defaultDeviceSerial = null,
		bool supported = true,
		string? lastFailureMessage = null)
		=> new(enabled,
			UsbConnectionsEnabled: true,
			Supported: supported,
			ResolvedExecutablePath: resolvedExecutablePath,
			ExecutableSource: executableSource,
			AdbVersion: adbVersion,
			ServerReachable: serverReachable,
			ServerStartedByMacroDeck: false,
			DefaultDeviceSerial: defaultDeviceSerial,
			LastFailure: null,
			LastFailureMessage: lastFailureMessage,
			LastFailureAt: null,
			PreviousShutdownWasUnclean: false,
			StaleTunnelsCleaned: 0);

	private static AdbDevice SampleDevice(string serial,
		AdbDeviceState state = AdbDeviceState.Device,
		string? model = "Pixel 7",
		AdbTunnel? tunnel = null)
		=> new(serial,
			state,
			model,
			Manufacturer: "Google",
			Product: "panther",
			TransportId: "1",
			Tunnel: tunnel,
			LastSeenAt: DateTimeOffset.UtcNow);

	[Test]
	public async Task Get_projects_settings_status_and_devices()
	{
		var fixture = CreateFixture();
		fixture.Repository.Seed(AppPreferenceService.AdbEnabledKey, "True");
		fixture.Repository.Seed(AppPreferenceService.AdbExecutablePathKey, "/usr/local/bin/adb");
		fixture.Repository.Seed(AppPreferenceService.AdbUsbConnectionsEnabledKey, "True");
		fixture.Repository.Seed(AppPreferenceService.AdbDefaultDeviceSerialKey, "R58M12ABCDE");

		fixture.AdbManager.Status = SampleStatus(defaultDeviceSerial: "R58M12ABCDE");
		fixture.AdbManager.Devices =
		[
			SampleDevice("R58M12ABCDE", tunnel: new AdbTunnel(true, 8193, 55001, null, null)),
			SampleDevice("OTHERSERIAL", state: AdbDeviceState.Unauthorized)
		];

		var response = await fixture.Get.Handle(new GetAdbSettingsRequest(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Enabled, Is.True);
			Assert.That(response.ExecutablePath, Is.EqualTo("/usr/local/bin/adb"));
			Assert.That(response.UsbConnectionsEnabled, Is.True);
			Assert.That(response.DefaultDeviceSerial, Is.EqualTo("R58M12ABCDE"));
			Assert.That(response.ResolvedExecutablePath, Is.EqualTo("/usr/local/bin/adb"));
			Assert.That(response.ExecutableSource, Is.EqualTo(nameof(AdbExecutableSource.Configured)));
			Assert.That(response.AdbVersion, Is.EqualTo("1.0.41"));
			Assert.That(response.ServerReachable, Is.True);
			Assert.That(response.ActivePublicPort, Is.EqualTo(ActivePort));
			Assert.That(response.DeviceSidePortCandidates, Is.EqualTo(AdbUsbTunnelPorts.DeviceSideCandidates));
			Assert.That(response.Devices, Has.Count.EqualTo(2));

			var defaultDevice = response.Devices.Single(device => device.Serial == "R58M12ABCDE");
			Assert.That(defaultDevice.IsDefault, Is.True);
			Assert.That(defaultDevice.Authorized, Is.True);
			Assert.That(defaultDevice.State, Is.EqualTo(nameof(AdbDeviceState.Device)));
			Assert.That(defaultDevice.TunnelEstablished, Is.True);
			Assert.That(defaultDevice.TunnelDevicePort, Is.EqualTo(8193));

			var otherDevice = response.Devices.Single(device => device.Serial == "OTHERSERIAL");
			Assert.That(otherDevice.IsDefault, Is.False);
			Assert.That(otherDevice.Authorized, Is.False);
			Assert.That(otherDevice.State, Is.EqualTo(nameof(AdbDeviceState.Unauthorized)));
			Assert.That(otherDevice.TunnelEstablished, Is.False);
		});
	}

	[Test]
	public async Task Update_with_a_missing_executable_path_fails_without_persisting()
	{
		var fixture = CreateFixture();
		var missingPath = Path.Combine(Path.GetTempPath(), $"missing-adb-{Guid.NewGuid():N}.exe");

		var response = await fixture.Update.Handle(
			new UpdateAdbSettingsRequest { Enabled = true, ExecutablePath = missingPath },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error, Does.Contain(missingPath));
			Assert.That(fixture.Repository.Writes, Is.Zero);
			Assert.That(fixture.AdbManager.ApplySettingsCallCount, Is.Zero);
		});
	}

	[Test]
	public async Task Update_with_an_unknown_default_device_serial_succeeds_and_persists()
	{
		var fixture = CreateFixture();
		fixture.AdbManager.Devices = [];

		var response = await fixture.Update.Handle(
			new UpdateAdbSettingsRequest { Enabled = true, DefaultDeviceSerial = "UNPLUGGEDDEVICE1" },
			CancellationToken.None);
		var reloaded = await fixture.Get.Handle(new GetAdbSettingsRequest(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.True);
			Assert.That(response.Error, Is.Null);
			Assert.That(response.DefaultDeviceSerial, Is.EqualTo("UNPLUGGEDDEVICE1"));
			Assert.That(fixture.Repository.Writes, Is.GreaterThan(0));
			Assert.That(reloaded.DefaultDeviceSerial, Is.EqualTo("UNPLUGGEDDEVICE1"));
		});
	}

	[Test]
	public async Task Update_awaits_ApplySettingsAsync_and_returns_the_post_change_snapshot()
	{
		var fixture = CreateFixture();
		fixture.AdbManager.Status = SampleStatus(resolvedExecutablePath: "/before/adb", serverReachable: false);
		fixture.AdbManager.Devices = [SampleDevice("BEFORESERIAL")];
		fixture.AdbManager.NextSnapshot = (SampleStatus(resolvedExecutablePath: "/after/adb", serverReachable: true),
			[SampleDevice("AFTERSERIAL")]);

		var response = await fixture.Update.Handle(new UpdateAdbSettingsRequest { Enabled = true },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(fixture.AdbManager.ApplySettingsCallCount, Is.EqualTo(1));
			Assert.That(response.ResolvedExecutablePath, Is.EqualTo("/after/adb"));
			Assert.That(response.ServerReachable, Is.True);
			Assert.That(response.Devices.Select(device => device.Serial).Single(), Is.EqualTo("AFTERSERIAL"));
		});
	}

	[Test]
	public async Task RestartServer_maps_a_failed_result_to_Success_false_with_the_message()
	{
		var fixture = CreateFixture();
		fixture.AdbManager.RestartServerResult =
			Result.Fail<AdbFailureCode>(AdbFailureCode.ServerUnreachable, "Failed to restart the adb server.");

		var response = await fixture.Restart.Handle(new RestartAdbServerRequest(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error, Is.EqualTo("Failed to restart the adb server."));
			Assert.That(fixture.AdbManager.RestartServerCallCount, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task RestartServer_reports_success_and_the_resulting_snapshot()
	{
		var fixture = CreateFixture();
		fixture.AdbManager.RestartServerResult = Result.Ok<AdbFailureCode>();
		fixture.AdbManager.NextSnapshot = (SampleStatus(serverReachable: true, adbVersion: "1.0.42"), []);

		var response = await fixture.Restart.Handle(new RestartAdbServerRequest(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.True);
			Assert.That(response.Error, Is.Null);
			Assert.That(response.ServerReachable, Is.True);
			Assert.That(response.AdbVersion, Is.EqualTo("1.0.42"));
		});
	}

	[Test]
	public async Task Download_writes_the_resolved_path_into_preferences_on_success_and_leaves_the_rest_untouched()
	{
		var fixture = CreateFixture();
		fixture.Repository.Seed(AppPreferenceService.AdbEnabledKey, "True");
		fixture.Repository.Seed(AppPreferenceService.AdbUsbConnectionsEnabledKey, "True");
		fixture.Repository.Seed(AppPreferenceService.AdbDefaultDeviceSerialKey, "R58M12ABCDE");
		fixture.Installer.InstallResult =
			Result.Ok<string, AdbFailureCode>("/opt/platform-tools/platform-tools/adb");

		var response = await fixture.Download.Handle(new DownloadAdbPlatformToolsRequest(), CancellationToken.None);
		var reloaded = await fixture.Get.Handle(new GetAdbSettingsRequest(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.True);
			Assert.That(response.Error, Is.Null);
			Assert.That(response.ExecutablePath, Is.EqualTo("/opt/platform-tools/platform-tools/adb"));
			Assert.That(reloaded.ExecutablePath, Is.EqualTo("/opt/platform-tools/platform-tools/adb"));
			Assert.That(reloaded.UsbConnectionsEnabled, Is.True, "an untouched field must keep its stored value");
			Assert.That(reloaded.DefaultDeviceSerial,
				Is.EqualTo("R58M12ABCDE"),
				"an untouched field must keep its stored value");
			Assert.That(fixture.Installer.InstallCallCount, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task Download_calls_ApplySettingsAsync_after_persisting_so_the_projection_reflects_the_new_state()
	{
		var fixture = CreateFixture();
		fixture.AdbManager.Status = SampleStatus(resolvedExecutablePath: "/before/adb", serverReachable: false);
		fixture.Installer.InstallResult = Result.Ok<string, AdbFailureCode>("/after/platform-tools/adb");
		fixture.AdbManager.NextSnapshot =
			(SampleStatus(resolvedExecutablePath: "/after/platform-tools/adb", serverReachable: true), []);

		var response = await fixture.Download.Handle(new DownloadAdbPlatformToolsRequest(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(fixture.AdbManager.ApplySettingsCallCount, Is.EqualTo(1));
			Assert.That(response.ResolvedExecutablePath, Is.EqualTo("/after/platform-tools/adb"));
			Assert.That(response.ServerReachable, Is.True);
		});
	}

	[Test]
	public async Task Download_leaves_preferences_and_the_manager_untouched_on_failure()
	{
		var fixture = CreateFixture();
		fixture.Repository.Seed(AppPreferenceService.AdbEnabledKey, "True");
		fixture.Repository.Seed(AppPreferenceService.AdbExecutablePathKey, "/already/configured/adb");
		fixture.Installer.InstallResult = Result.Fail<string, AdbFailureCode>(AdbFailureCode.CommandFailed,
			"Android platform-tools could not be downloaded. Check your internet connection and try again.");

		var response = await fixture.Download.Handle(new DownloadAdbPlatformToolsRequest(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error,
				Is.EqualTo(
					"Android platform-tools could not be downloaded. Check your internet connection and try again."));
			Assert.That(response.ExecutablePath,
				Is.EqualTo("/already/configured/adb"),
				"the unchanged state must be reported");
			Assert.That(fixture.Repository.Writes, Is.Zero);
			Assert.That(fixture.AdbManager.ApplySettingsCallCount, Is.Zero);
		});
	}

	[Test]
	public async Task Download_reports_Unsupported_from_the_installer_without_persisting()
	{
		var fixture = CreateFixture();
		fixture.Installer.InstallResult = Result.Fail<string, AdbFailureCode>(AdbFailureCode.Unsupported,
			"Android platform-tools has no download available for this operating system.");

		var response = await fixture.Download.Handle(new DownloadAdbPlatformToolsRequest(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error, Does.Contain("no download available"));
			Assert.That(fixture.Repository.Writes, Is.Zero);
			Assert.That(fixture.AdbManager.ApplySettingsCallCount, Is.Zero);
		});
	}

	[Test]
	public async Task Update_leaves_omitted_fields_at_their_stored_values()
	{
		var fixture = CreateFixture();
		await fixture.Update.Handle(new UpdateAdbSettingsRequest
			{
				Enabled = true,
				ExecutablePath = null,
				UsbConnectionsEnabled = true,
				DefaultDeviceSerial = "R58M12ABCDE"
			},
			CancellationToken.None);

		var response = await fixture.Update.Handle(new UpdateAdbSettingsRequest { UsbConnectionsEnabled = false },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.UsbConnectionsEnabled, Is.False, "the field that was sent must change");
			Assert.That(response.Enabled, Is.True, "an omitted toggle must not be reset to its default");
			Assert.That(response.DefaultDeviceSerial, Is.EqualTo("R58M12ABCDE"));
		});
	}

	[Test]
	public async Task Update_clears_a_text_field_when_it_is_sent_as_an_empty_string()
	{
		var fixture = CreateFixture();
		await fixture.Update.Handle(
			new UpdateAdbSettingsRequest { Enabled = true, DefaultDeviceSerial = "R58M12ABCDE" },
			CancellationToken.None);

		var response = await fixture.Update.Handle(new UpdateAdbSettingsRequest { DefaultDeviceSerial = string.Empty },
			CancellationToken.None);

		Assert.That(response.DefaultDeviceSerial, Is.Null, "an empty string is how a text field is cleared");
	}

	// Standing guard against someone "helpfully" enriching the broadcast payload later: the send site
	// in AdbBackgroundService is only safe because this type carries nothing admin-gated.
	[Test]
	public void AdbStateChangedEvent_carries_no_device_serial_no_executable_path_and_no_port()
	{
		var properties = typeof(AdbStateChangedEvent).GetProperties(BindingFlags.Public | BindingFlags.Instance);

		Assert.Multiple(() =>
		{
			Assert.That(properties, Has.Length.EqualTo(1), "Unexpected new property on AdbStateChangedEvent.");

			foreach (var property in properties)
			{
				var name = property.Name.ToLowerInvariant();
				Assert.That(name, Does.Not.Contain("serial"), $"{property.Name} looks like a device serial.");
				Assert.That(name, Does.Not.Contain("path"), $"{property.Name} looks like an executable path.");
				Assert.That(name, Does.Not.Contain("port"), $"{property.Name} looks like a port.");
				Assert.That(name, Does.Not.Contain("device"), $"{property.Name} looks like device data.");
			}
		});
	}
}
