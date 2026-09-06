using MacroDeckHost.Application.Services;
using MacroDeck.Localization;
using Microsoft.Extensions.DependencyInjection;
using MacroDeckHost.Application.Adb;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.ClientTargets;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Infrastructure.ClientTargets.CarThing;
using MacroDeckHost.Localization;
using MacroDeckHost.Tests.UnitTests.Adb;

namespace MacroDeckHost.Tests.UnitTests.ClientTargets;

/// <summary>
/// The Car Thing setup workflow (issue #727). The device half cannot be exercised anywhere, so what
/// is pinned here is everything up to the adb argument vector: which device is chosen, which steps
/// the user is walked through, what is written, and - the security invariant carried over from
/// ADR 0030 - that provisioning never points the device at the loopback listener.
/// </summary>
[TestFixture]
public class CarThingProvisionerTests
{
	private const string Serial = "SUPERBIRD01";

	private static readonly AdbStatus _ready = AdbStatus.Disabled with
	{
		Enabled = true,
		UsbConnectionsEnabled = true
	};

	[Test]
	public async Task Provisioning_is_unavailable_until_adb_carries_usb_connections()
	{
		var manager = new FakeAdbManager { Status = AdbStatus.Disabled with { Enabled = true } };
		var provisioner = Create(manager);

		var available = await provisioner.IsAvailableAsync(CancellationToken.None);

		Assert.That(available,
			Is.False,
			"the whole workflow runs over the USB tunnel, so offering it without USB connections would fail on the first step that matters");
	}

	[TestCase("Car_Thing", Description = "the model a real jailbroken device reports over adb")]
	[TestCase("spotify-car-thing", Description = "the product the same device reports")]
	[TestCase("Car Thing")]
	[TestCase("CarThing")]
	[TestCase("carthing")]
	[TestCase("superbird")]
	[TestCase("Superbird")]
	public async Task A_car_thing_is_recognised_however_its_firmware_spells_the_board(string model)
	{
		// Separators differ between the community firmwares, and the first real device this was tried
		// against reported Car_Thing - which an exact-marker match missed entirely.
		var manager = new FakeAdbManager
		{
			Status = _ready,
			Devices = [Device(model: model, tunnelPort: 8193)]
		};
		var provisioner = Create(manager);

		var result = await provisioner.AdvanceAsync(CarThingProvisioner.StepDetect,
			new Dictionary<string, string>(),
			CancellationToken.None);

		Assert.That(result.Kind, Is.EqualTo(WebClientTargetProvisioningResultKind.Step));
	}

	[Test]
	public async Task A_car_thing_adb_cannot_reach_is_reported_as_such_rather_than_as_missing()
	{
		// "No Car Thing found. Check the cable" sends the user after a cable when adb has already
		// listed the device and simply cannot talk to it.
		var manager = new FakeAdbManager
		{
			Status = _ready,
			Devices =
			[
				Device(model: "Car_Thing",
					tunnelPort: null,
					state: AdbDeviceState.Unauthorized)
			]
		};
		var provisioner = Create(manager);

		var result = await provisioner.AdvanceAsync(CarThingProvisioner.StepDetect,
			new Dictionary<string, string>(),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(WebClientTargetProvisioningResultKind.Error));
			Assert.That(result.Message.Localized?.Key.ToString(),
				Is.Not.EqualTo(AppStrings.ClientTargets.CarThing.Detect.NotFound().Key.ToString()),
				"an unreachable device is a different problem from an absent one");
			Assert.That(result.Message.Localized?.Arguments.ContainsKey("state"),
				Is.True,
				"the message has to name the state adb reported, or it explains nothing");
		});
	}

	[Test]
	public async Task A_shell_error_in_the_manufacturer_field_never_identifies_a_device()
	{
		// A jailbroken Car Thing runs Linux and has no getprop, so that field carries whatever the
		// device shell printed. It is not identity and must not be matched on.
		var manager = new FakeAdbManager
		{
			Status = _ready,
			Devices =
			[
				Device(model: "Pixel 8",
					tunnelPort: 8193,
					manufacturer: "/bin/sh: getprop: not found")
			]
		};
		var provisioner = Create(manager);

		var result = await provisioner.AdvanceAsync(CarThingProvisioner.StepDetect,
			new Dictionary<string, string>(),
			CancellationToken.None);

		Assert.That(result.Kind, Is.EqualTo(WebClientTargetProvisioningResultKind.Error));
	}

	[Test]
	public async Task A_device_that_is_not_a_car_thing_does_not_advance_past_detection()
	{
		var manager = new FakeAdbManager
		{
			Status = _ready,
			Devices = [Device(model: "Pixel 8", tunnelPort: 8193)]
		};
		var provisioner = Create(manager);

		var result = await provisioner.AdvanceAsync(CarThingProvisioner.StepDetect,
			new Dictionary<string, string>(),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(WebClientTargetProvisioningResultKind.Error));
			Assert.That(result.Step!.StepId,
				Is.EqualTo(CarThingProvisioner.StepDetect),
				"the user has to stay on the step they can still act on");
		});
	}

	[Test]
	public async Task A_car_thing_without_a_tunnel_is_not_configured()
	{
		var manager = new FakeAdbManager
		{
			Status = _ready,
			Devices = [Device(model: "Superbird", tunnelPort: null)]
		};
		var provisioner = Create(manager);

		var result = await provisioner.AdvanceAsync(CarThingProvisioner.StepConnect,
			new Dictionary<string, string>(),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(WebClientTargetProvisioningResultKind.Error));
			Assert.That(manager.ExecutedCommands,
				Is.Empty,
				"nothing may be written to a device Macro Deck cannot be reached from");
		});
	}

	[Test]
	public async Task Configuring_writes_the_tunnel_address_of_the_target_build_to_the_device()
	{
		var scratch = new FakeScratchWriter();
		var manager = new FakeAdbManager
		{
			Status = _ready,
			Devices = [Device(model: "Superbird", tunnelPort: 8195)]
		};
		var provisioner = Create(manager, scratch);

		var result = await provisioner.AdvanceAsync(CarThingProvisioner.StepConfigure,
			new Dictionary<string, string>(),
			CancellationToken.None);

		var push = manager.ExecutedCommands.OfType<AdbPushFileCommand>().Single();
		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(WebClientTargetProvisioningResultKind.Complete));
			Assert.That(manager.ExecutedCommands.TakeWhile(command => command is not AdbPushFileCommand)
					.OfType<AdbRemountRootWritableCommand>(),
				Is.Not.Empty,
				"the device ships its root filesystem read-only, so the write has to be unlocked first");
			Assert.That(scratch.WrittenContents.Single(),
				Does.Contain("http://127.0.0.1:8195/targets/carthing/"),
				"the device dials its own end of the reverse tunnel, and the path is where the host serves the target build");
			Assert.That(push.DevicePath,
				Is.EqualTo("/var/www/index.html"),
				"the kiosk browser already serves this directory, so replacing its page is what redirects the device");
			Assert.That(scratch.DeletedPaths,
				Does.Contain(scratch.PathToReturn),
				"the temporary file exists only for the transfer");
		});
	}

	[Test]
	public async Task A_firmware_that_keeps_its_configuration_elsewhere_is_a_field_edit()
	{
		var manager = new FakeAdbManager
		{
			Status = _ready,
			Devices = [Device(model: "Superbird", tunnelPort: 8193)]
		};
		var provisioner = Create(manager);

		await provisioner.AdvanceAsync(CarThingProvisioner.StepConfigure,
			new Dictionary<string, string>
			{
				[CarThingProvisioner.FieldWebRoot] = "/opt/kiosk/www",
				[CarThingProvisioner.FieldServiceName] = "nocturne",
				[CarThingProvisioner.FieldServiceManager] = nameof(AdbServiceManager.SysVInit)
			},
			CancellationToken.None);

		var push = manager.ExecutedCommands.OfType<AdbPushFileCommand>().Single();
		var restart = manager.ExecutedCommands.OfType<AdbRestartServiceCommand>().Single();
		Assert.Multiple(() =>
		{
			Assert.That(push.DevicePath, Is.EqualTo("/opt/kiosk/www/index.html"));
			Assert.That(restart.ServiceName, Is.EqualTo("nocturne"));
			Assert.That(restart.Manager, Is.EqualTo(AdbServiceManager.SysVInit));
		});
	}

	[Test]
	public async Task The_kiosk_is_restarted_through_supervisord_by_default()
	{
		// The community firmwares run the Car Thing's browser as a supervisord program called chromium;
		// systemd is not what this device uses.
		var manager = new FakeAdbManager
		{
			Status = _ready,
			Devices = [Device(model: "Car_Thing", tunnelPort: 8193)]
		};
		var provisioner = Create(manager);

		await provisioner.AdvanceAsync(CarThingProvisioner.StepConfigure,
			new Dictionary<string, string>(),
			CancellationToken.None);

		var restart = manager.ExecutedCommands.OfType<AdbRestartServiceCommand>().Single();
		Assert.Multiple(() =>
		{
			Assert.That(restart.Manager, Is.EqualTo(AdbServiceManager.Supervisord));
			Assert.That(restart.ServiceName, Is.EqualTo("chromium"));
		});
	}

	[Test]
	public async Task Setup_fills_its_own_fields_in_from_what_the_device_reports()
	{
		// The point of detection: the user should not have to know their firmware's web root or the
		// name of its browser process.
		var manager = new FakeAdbManager
		{
			Status = _ready,
			Devices = [Device(model: "Car_Thing", tunnelPort: 8193)],
			QueryResultFor = command => command switch
			{
				AdbListServicesCommand => Result.Ok<string, AdbFailureCode>(
					"backlight   RUNNING\nchromium   RUNNING\npython-web-server   RUNNING\n"),
				AdbDirectoryExistsCommand { DevicePath: "/var/www" } =>
					Result.Ok<string, AdbFailureCode>(string.Empty),
				_ => Result.Fail<string, AdbFailureCode>(AdbFailureCode.CommandFailed, "no such directory")
			}
		};
		var provisioner = Create(manager);

		var result = await provisioner.AdvanceAsync(CarThingProvisioner.StepConnect,
			new Dictionary<string, string>(),
			CancellationToken.None);

		var fields = result.Step!.Fields.ToDictionary(field => field.FieldId, field => field.DefaultValue);
		Assert.Multiple(() =>
		{
			Assert.That(fields[CarThingProvisioner.FieldWebRoot], Is.EqualTo("/var/www"));
			Assert.That(fields[CarThingProvisioner.FieldServiceName], Is.EqualTo("chromium"));
			Assert.That(fields[CarThingProvisioner.FieldServiceManager],
				Is.EqualTo(nameof(AdbServiceManager.Supervisord)));
		});
	}

	[Test]
	public async Task Detection_skips_the_renamed_leftover_and_finds_the_directory_that_is_served()
	{
		// Ground truth from a real device: `python -m http.server 8080 -d /var/www` is what answers the
		// kiosk, while the original Spotify webapp sits renamed beside it. Writing into the leftover
		// would change nothing on screen - which is exactly what an earlier attempt did.
		var manager = new FakeAdbManager
		{
			Status = _ready,
			Devices = [Device(model: "Car_Thing", tunnelPort: 8193)],
			QueryResultFor = command => command switch
			{
				AdbListServicesCommand => Result.Ok<string, AdbFailureCode>(
					"backlight   RUNNING\nchromium   RUNNING\npython-web-server   RUNNING\n"),
				// Only these two exist on the device this was read from.
				AdbDirectoryExistsCommand { DevicePath: "/var/www" } =>
					Result.Ok<string, AdbFailureCode>(string.Empty),
				AdbDirectoryExistsCommand { DevicePath: "/usr/share/qt-superbird-app/webapp.bak" } =>
					Result.Ok<string, AdbFailureCode>(string.Empty),
				_ => Result.Fail<string, AdbFailureCode>(AdbFailureCode.CommandFailed, "no such directory")
			}
		};
		var provisioner = Create(manager);

		var result = await provisioner.AdvanceAsync(CarThingProvisioner.StepConnect,
			new Dictionary<string, string>(),
			CancellationToken.None);

		var webRoot = result.Step!.Fields
			.Single(field => field.FieldId == CarThingProvisioner.FieldWebRoot).DefaultValue;
		Assert.That(webRoot, Is.EqualTo("/var/www"));
	}

	[Test]
	public async Task Detection_costs_the_device_only_a_couple_of_read_only_commands()
	{
		// A minimal adbd does not survive being interrogated - that is the whole reason the reconcile
		// loop and the property probe stopped doing it.
		var manager = new FakeAdbManager
		{
			Status = _ready,
			Devices = [Device(model: "Car_Thing", tunnelPort: 8193)],
			QueryResultFor = command => command is AdbListServicesCommand
				? Result.Ok<string, AdbFailureCode>("chromium   RUNNING\n")
				: Result.Ok<string, AdbFailureCode>(string.Empty)
		};
		var provisioner = Create(manager);

		await provisioner.AdvanceAsync(CarThingProvisioner.StepConnect,
			new Dictionary<string, string>(),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(manager.Queries, Has.Count.LessThanOrEqualTo(4));
			Assert.That(manager.Queries,
				Has.All.InstanceOf<AdbQueryCommand>(),
				"detection must never reach for anything that writes");
			Assert.That(manager.ExecutedCommands, Is.Empty, "and it must not run an action at all");
		});
	}

	[Test]
	public async Task A_device_that_answers_nothing_still_offers_workable_defaults()
	{
		var manager = new FakeAdbManager
		{
			Status = _ready,
			Devices = [Device(model: "Car_Thing", tunnelPort: 8193)],
			QueryResultFor = _ => Result.Fail<string, AdbFailureCode>(AdbFailureCode.CommandFailed, "nope")
		};
		var provisioner = Create(manager);

		var result = await provisioner.AdvanceAsync(CarThingProvisioner.StepConnect,
			new Dictionary<string, string>(),
			CancellationToken.None);

		var fields = result.Step!.Fields.ToDictionary(field => field.FieldId, field => field.DefaultValue);
		Assert.That(fields[CarThingProvisioner.FieldServiceName],
			Is.Not.Empty,
			"a device that will not answer must still leave the user something to correct");
	}

	[Test]
	public async Task The_page_written_to_the_device_carries_a_one_time_credential()
	{
		// The Car Thing has no keyboard, so nobody can sign it in. Setup runs on the loopback listener
		// with admin, which is the one position from which handing a device a session is legitimate.
		var scratch = new FakeScratchWriter();
		var auth = new FakeEnrollmentAuthService { TokenToIssue = "tok-123" };
		var manager = new FakeAdbManager
		{
			Status = _ready,
			Devices = [Device(model: "Car_Thing", tunnelPort: 8193)]
		};
		var provisioner = Create(manager, scratch, auth);

		await provisioner.AdvanceAsync(CarThingProvisioner.StepConfigure,
			new Dictionary<string, string>(),
			CancellationToken.None);

		var page = scratch.WrittenContents.Single();
		Assert.Multiple(() =>
		{
			Assert.That(page,
				Does.Contain("#enroll=tok-123"),
				"in the fragment, so it never reaches the host's logs");
			Assert.That(auth.MintedLifetimes.Single(), Is.EqualTo(AuthDefaults.DeviceEnrollmentLifetime));
		});
	}

	[Test]
	public async Task A_device_is_still_pointed_at_the_client_when_no_credential_could_be_minted()
	{
		var scratch = new FakeScratchWriter();
		var auth = new FakeEnrollmentAuthService { MintSucceeds = false };
		var manager = new FakeAdbManager
		{
			Status = _ready,
			Devices = [Device(model: "Car_Thing", tunnelPort: 8193)]
		};
		var provisioner = Create(manager, scratch, auth);

		await provisioner.AdvanceAsync(CarThingProvisioner.StepConfigure,
			new Dictionary<string, string>(),
			CancellationToken.None);

		var page = scratch.WrittenContents.Single();
		Assert.Multiple(() =>
		{
			Assert.That(page, Does.Contain("http://127.0.0.1:8193/targets/carthing/"));
			Assert.That(page,
				Does.Not.Contain("#enroll="),
				"a device that cannot be enrolled still gets the client, just with a sign-in it has to be given some other way");
		});
	}

	[Test]
	public async Task The_page_waits_for_the_host_instead_of_navigating_into_nothing()
	{
		// The device boots faster than the host establishes its reverse tunnel. A page that jumps
		// straight there lands on a failed connection and stays on it - a blank screen with no way
		// back, which is exactly what a real Car Thing showed.
		var scratch = new FakeScratchWriter();
		var manager = new FakeAdbManager
		{
			Status = _ready,
			Devices = [Device(model: "Car_Thing", tunnelPort: 8193)]
		};
		var provisioner = Create(manager, scratch);

		await provisioner.AdvanceAsync(CarThingProvisioner.StepConfigure,
			new Dictionary<string, string>(),
			CancellationToken.None);

		var page = scratch.WrittenContents.Single();
		Assert.Multiple(() =>
		{
			Assert.That(page,
				Does.Contain("/api/system/build-info"),
				"it has to ask whether the host is there before going");
			Assert.That(page,
				Does.Not.Contain("http-equiv=\"refresh\""),
				"a meta refresh would fire immediately and defeat the waiting");
			Assert.That(page,
				Does.Contain("ClientTargets.CarThing.Waiting"),
				"and say what it is doing, so the screen is not simply blank");
		});
	}

	[Test]
	public async Task A_failed_restart_still_reports_the_address_that_was_written()
	{
		var manager = new FakeAdbManager
		{
			Status = _ready,
			Devices = [Device(model: "Superbird", tunnelPort: 8193)],
			ExecuteResultFor = command => command is AdbRestartServiceCommand
				? Result.Fail<AdbFailureCode>(AdbFailureCode.CommandFailed, "no such service")
				: Result.Ok<AdbFailureCode>()
		};
		var provisioner = Create(manager);

		var result = await provisioner.AdvanceAsync(CarThingProvisioner.StepConfigure,
			new Dictionary<string, string>(),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(WebClientTargetProvisioningResultKind.Error));
			Assert.That(result.Step!.StepId,
				Is.EqualTo(CarThingProvisioner.StepDone),
				"the address is on the device either way, so the user must not be sent back through steps that already succeeded");
		});
	}

	[Test]
	public async Task No_provisioning_step_ever_points_the_device_at_the_loopback_listener()
	{
		// ADR 0030: a request arriving through the tunnel comes from 127.0.0.1 and would be granted
		// synthetic admin if it landed on the loopback listener. The device-side port is the tunnel's
		// own end, never a host port, and this pins that for every step that writes anything.
		var scratch = new FakeScratchWriter();
		var manager = new FakeAdbManager
		{
			Status = _ready,
			Devices = [Device(model: "Superbird", tunnelPort: 8193)]
		};
		var provisioner = Create(manager, scratch);

		foreach (var step in new[]
			{
				CarThingProvisioner.StepDetect, CarThingProvisioner.StepPrepare,
				CarThingProvisioner.StepConnect, CarThingProvisioner.StepConfigure
			})
		{
			await provisioner.AdvanceAsync(step, new Dictionary<string, string>(), CancellationToken.None);
		}

		Assert.Multiple(() =>
		{
			Assert.That(scratch.WrittenContents,
				Has.All.Contains($"127.0.0.1:{AdbUsbTunnelPorts.DeviceSideCandidates[0]}"));
			Assert.That(scratch.WrittenContents,
				Has.None.Contains("5191"),
				"the development loopback port must never reach the device");
			Assert.That(scratch.WrittenContents,
				Has.None.Contains("8191"),
				"nor the production loopback port");
		});
	}

	[Test]
	public async Task An_unknown_step_id_restarts_the_walkthrough_rather_than_failing()
	{
		var manager = new FakeAdbManager { Status = _ready };
		var provisioner = Create(manager);

		var result = await provisioner.AdvanceAsync("a-step-from-a-previous-run",
			new Dictionary<string, string>(),
			CancellationToken.None);

		Assert.That(result.Step!.StepId, Is.EqualTo(CarThingProvisioner.StepDetect));
	}

	private static CarThingProvisioner Create(
		FakeAdbManager manager,
		FakeScratchWriter? scratch = null,
		FakeEnrollmentAuthService? authService = null)
		=> new(manager,
			scratch ?? new FakeScratchWriter(),
			authService ?? new FakeEnrollmentAuthService(),
			ScopeFactory());

	/// <summary>Just enough container for the one thing provisioning resolves: the waiting text.</summary>
	private static IServiceScopeFactory ScopeFactory()
	{
		var services = new ServiceCollection();
		services.AddSingleton<ILocalizationResolver, FakeLocalizationResolver>();
		services.AddScoped<IAppPreferenceService>(_ => new FakeAdbPreferenceService());
		return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
	}

	private static AdbDevice Device(
		string model,
		int? tunnelPort,
		AdbDeviceState state = AdbDeviceState.Device,
		string? manufacturer = "Spotify")
		=> new(Serial,
			state,
			model,
			Manufacturer: manufacturer,
			Product: model,
			TransportId: "1",
			Tunnel: tunnelPort is { } port ? new AdbTunnel(true, port, 8194, null, null) : null,
			LastSeenAt: DateTimeOffset.UnixEpoch);
}
