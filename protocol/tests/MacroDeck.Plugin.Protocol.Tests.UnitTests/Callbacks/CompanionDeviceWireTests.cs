using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Companion;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Callbacks;

[TestFixture]
public class CompanionDeviceWireTests
{
	private static readonly Guid _deviceId = Guid.Parse("4f1c2a3b-5d6e-4f70-8192-a3b4c5d6e7f8");
	private static readonly string[] _screenOnAndUnknown = ["screenOn", "hologram"];

	[Test]
	public void A_pushed_device_travels_under_its_frozen_wire_names()
	{
		var json = JsonSerializer.SerializeToElement(new CompanionDevicesStateDto
			{
				Access = CompanionDeviceAccessStates.Available,
				Devices =
				[
					CompanionDeviceWire.ToDto(new CompanionDevice(_deviceId,
						7,
						new CompanionDeviceState
						{
							BatteryLevelPercent = 80,
							IsCharging = true,
							Orientation = CompanionOrientations.Portrait,
							Capabilities = new HashSet<string> { CompanionDeviceCapabilities.ScreenOn },
							RequestableCapabilities
								= new HashSet<string> { CompanionDeviceCapabilities.ScreenshotFull },
							ConfirmsCommands = true,
							NetworkType = CompanionNetworkTypes.Wifi,
							FormFactor = CompanionFormFactors.Tablet
						}))
				],
				Revision = 3
			},
			PluginProtocolJson.Options);

		var device = json.GetProperty("devices")[0];
		Assert.Multiple(() =>
		{
			Assert.That(json.GetProperty("access").GetString(), Is.EqualTo("available"));
			Assert.That(json.GetProperty("revision").GetInt64(), Is.EqualTo(3));
			Assert.That(device.GetProperty("deviceId").GetString(), Is.EqualTo(_deviceId.ToString("D")));
			Assert.That(device.GetProperty("readySequence").GetInt64(), Is.EqualTo(7));
			Assert.That(device.GetProperty("batteryLevelPercent").GetInt32(), Is.EqualTo(80));
			Assert.That(device.GetProperty("charging").GetBoolean(), Is.True);
			Assert.That(device.GetProperty("orientation").GetString(), Is.EqualTo("portrait"));
			Assert.That(device.GetProperty("capabilities")[0].GetString(), Is.EqualTo("screenOn"));
			Assert.That(device.GetProperty("requestableCapabilities")[0].GetString(), Is.EqualTo("screenshotFull"));
			Assert.That(device.GetProperty("confirmsCommands").GetBoolean(), Is.True);
			Assert.That(device.GetProperty("networkType").GetString(), Is.EqualTo("wifi"));
			Assert.That(device.GetProperty("formFactor").GetString(), Is.EqualTo("tablet"));
			Assert.That(device.TryGetProperty("model", out _), Is.False);
		});
	}

	[Test]
	public void A_device_read_back_keeps_capability_names_this_version_does_not_know()
	{
		var device = CompanionDeviceWire.ToDevice(new CompanionDeviceStateDto
		{
			DeviceId = _deviceId.ToString("D"),
			ReadySequence = 2,
			Capabilities = _screenOnAndUnknown
		});

		Assert.That(device!.State.Capabilities, Is.EquivalentTo(_screenOnAndUnknown));
	}

	[Test]
	public void A_device_pushed_by_a_host_without_form_factors_reads_back_without_one()
	{
		var dto = JsonSerializer.Deserialize<CompanionDeviceStateDto>(
			$$"""{"deviceId":"{{_deviceId:D}}","readySequence":1,"model":"Pixel 8","platform":"Android 14"}""",
			PluginProtocolJson.Options)!;

		var device = CompanionDeviceWire.ToDevice(dto)!;

		Assert.Multiple(() =>
		{
			Assert.That(device.State.Model, Is.EqualTo("Pixel 8"));
			Assert.That(device.State.FormFactor, Is.Null);
		});
	}

	[Test]
	public void A_device_without_a_form_factor_is_pushed_without_one()
	{
		var json = JsonSerializer.SerializeToElement(
			CompanionDeviceWire.ToDto(new CompanionDevice(_deviceId, 1, new CompanionDeviceState())),
			PluginProtocolJson.Options);

		Assert.That(json.TryGetProperty("formFactor", out _), Is.False);
	}

	[Test]
	public void A_pushed_device_without_a_guid_is_skipped()
	{
		Assert.That(CompanionDeviceWire.ToDevice(new CompanionDeviceStateDto { DeviceId = "phone" }), Is.Null);
	}

	[Test]
	public void Every_command_round_trips_through_its_wire_arguments()
	{
		CompanionDeviceCommand[] commands =
		[
			CompanionDeviceCommand.SetBrightness(40),
			CompanionDeviceCommand.SetOrientation(CompanionOrientations.Landscape),
			CompanionDeviceCommand.Vibrate,
			CompanionDeviceCommand.ScreenOn,
			CompanionDeviceCommand.ScreenOff,
			CompanionDeviceCommand.Focus
		];

		Assert.That(commands.Select(command
				=> CompanionDeviceWire.ToCommand(CompanionDeviceWire.ToArguments(_deviceId, command))),
			Is.EqualTo(commands));
	}

	[TestCase("setBrightness", 101, null)]
	[TestCase("setBrightness", null, null)]
	[TestCase("setOrientation", null, "upside-down")]
	[TestCase("screenshot", null, null)]
	[TestCase("cancelRequest", null, null)]
	public void A_command_the_host_does_not_offer_or_with_invalid_values_reads_as_none(
		string command,
		int? brightness,
		string? orientation)
	{
		Assert.That(CompanionDeviceWire.ToCommand(new CompanionDeviceCommandArguments
			{
				DeviceId = _deviceId.ToString("D"),
				Command = command,
				BrightnessPercent = brightness,
				Orientation = orientation
			}),
			Is.Null);
	}

	[Test]
	public void A_failure_or_outcome_from_a_newer_host_reads_as_failed()
	{
		Assert.Multiple(() =>
		{
			Assert.That(CompanionDeviceWire.ToFailure("melted"), Is.EqualTo(CompanionDeviceFailure.Failed));
			Assert.That(CompanionDeviceWire.ToResult(new CompanionDeviceRequestResultDto { Outcome = "maybe" }),
				Is.EqualTo(CompanionDeviceCommandResult.Failed(CompanionDeviceFailure.Failed)));
		});
	}

	[TestCase(CompanionDeviceFailure.NotConnected, "not-connected")]
	[TestCase(CompanionDeviceFailure.Removed, "removed")]
	[TestCase(CompanionDeviceFailure.TimedOut, "timed-out")]
	[TestCase(CompanionDeviceFailure.ConsentDenied, "consent-denied")]
	[TestCase(CompanionDeviceFailure.Unavailable, "unavailable")]
	[TestCase(CompanionDeviceFailure.Failed, "failed")]
	public void Failures_keep_their_frozen_wire_values(CompanionDeviceFailure failure, string wire)
	{
		Assert.Multiple(() =>
		{
			Assert.That(CompanionDeviceWire.ToWire(failure), Is.EqualTo(wire));
			Assert.That(CompanionDeviceWire.ToFailure(wire), Is.EqualTo(failure));
		});
	}

	[Test]
	public void Results_keep_their_outcome_across_the_wire()
	{
		Assert.That(new[]
			{
				CompanionDeviceCommandResult.Done,
				CompanionDeviceCommandResult.Unconfirmed,
				CompanionDeviceCommandResult.Failed(CompanionDeviceFailure.ConsentDenied)
			}.Select(result => CompanionDeviceWire.ToResult(CompanionDeviceWire.ToDto(result))),
			Is.EqualTo(new[]
			{
				CompanionDeviceCommandResult.Done,
				CompanionDeviceCommandResult.Unconfirmed,
				CompanionDeviceCommandResult.Failed(CompanionDeviceFailure.ConsentDenied)
			}));
	}
}
