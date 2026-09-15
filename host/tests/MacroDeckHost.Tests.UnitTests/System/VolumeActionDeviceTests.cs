using MacroDeckHost.Integrations.System.Actions;
using MacroDeckHost.Integrations.System.Volume;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Tests.UnitTests.System;

public class VolumeActionDeviceTests
{
	private static readonly AudioTarget _headset = new(AudioFlow.Output, "headset-uid");
	private static readonly AudioTarget _microphone = new(AudioFlow.Input, "AppleUSBAudioEngine:Vendor:Mic:1:1");

	private static ActionExecutionContext Context(string? device, params (string Name, object Value)[] values)
	{
		var parameters = values.ToDictionary(value => value.Name, value => value.Value);
		if (device is not null)
		{
			parameters["device"] = device;
		}

		return new ActionExecutionContext { Parameters = parameters };
	}

	[Test]
	public async Task Increasing_the_volume_of_a_device_leaves_the_default_output_alone()
	{
		var volume = new FakeVolumeService { Volume = 0.5f };
		volume.Set(_headset, 0.2f, false);

		await new IncreaseVolumeActionDefinition(volume).CreateExecutor()
			.ExecuteAsync(Context(_headset.ToParameterValue(), ("amount", 10)));

		Assert.Multiple(() =>
		{
			Assert.That(volume.VolumeOf(_headset)!.Value, Is.EqualTo(0.3f).Within(0.0001));
			Assert.That(volume.Volume, Is.EqualTo(0.5f));
		});
	}

	[Test]
	public async Task Decreasing_the_default_input_changes_the_microphone_only()
	{
		var volume = new FakeVolumeService { Volume = 0.5f };
		volume.Set(AudioTarget.DefaultInput, 0.6f, false);

		await new DecreaseVolumeActionDefinition(volume).CreateExecutor()
			.ExecuteAsync(Context("default-input", ("amount", 20)));

		Assert.Multiple(() =>
		{
			Assert.That(volume.VolumeOf(AudioTarget.DefaultInput)!.Value, Is.EqualTo(0.4f).Within(0.0001));
			Assert.That(volume.Volume, Is.EqualTo(0.5f));
		});
	}

	[Test]
	public async Task Muting_a_specific_microphone_toggles_that_microphone_only()
	{
		var volume = new FakeVolumeService { Muted = false };
		volume.Set(_microphone, 0.5f, false);

		var result = await new MuteVolumeActionDefinition(volume).CreateExecutor()
			.ExecuteAsync(Context(_microphone.ToParameterValue()));

		Assert.Multiple(() =>
		{
			Assert.That(result.ExpectedStateId, Is.EqualTo("muted"));
			Assert.That(volume.MutedOf(_microphone), Is.True);
			Assert.That(volume.Muted, Is.False);
		});
	}

	[Test]
	public async Task The_mute_button_state_follows_the_selected_device()
	{
		var volume = new FakeVolumeService { Muted = false };
		volume.Set(_microphone, 0.5f, true);
		var action = new MuteVolumeActionDefinition(volume);

		var microphone = await action.GetActionStateAsync(
			new Dictionary<string, object?> { ["device"] = _microphone.ToParameterValue() },
			CancellationToken.None);
		var speakers = await action.GetActionStateAsync(new Dictionary<string, object?>(), CancellationToken.None);
		var unplugged = await action.GetActionStateAsync(
			new Dictionary<string, object?> { ["device"] = "output:unplugged-uid" },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(microphone?.ActiveStateId, Is.EqualTo("muted"));
			Assert.That(speakers?.ActiveStateId, Is.EqualTo("unmuted"));
			Assert.That(unplugged?.ActiveStateId, Is.EqualTo("unavailable"));
		});
	}

	[Test]
	public async Task Setting_the_volume_of_a_device_sets_that_device()
	{
		var volume = new FakeVolumeService();

		var result = await new SetVolumeActionDefinition(volume).CreateExecutor()
			.ExecuteAsync(Context(_headset.ToParameterValue(), ("level", 35)));

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.Null);
			Assert.That(volume.VolumeOf(_headset)!.Value, Is.EqualTo(0.35f).Within(0.0001));
		});
	}

	[Test]
	public async Task A_device_that_refuses_the_new_volume_is_reported_unavailable()
	{
		var volume = new FakeVolumeService { SetsSucceed = false };

		var result = await new SetVolumeActionDefinition(volume).CreateExecutor()
			.ExecuteAsync(Context(_headset.ToParameterValue(), ("level", 35)));

		Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.Unavailable));
	}

	[Test]
	public async Task An_unrecognised_device_value_fails_without_touching_the_speakers()
	{
		var volume = new FakeVolumeService { Volume = 0.5f, Muted = false };

		var set = await new SetVolumeActionDefinition(volume).CreateExecutor()
			.ExecuteAsync(Context("speakers", ("level", 10)));
		var mute = await new MuteVolumeActionDefinition(volume).CreateExecutor().ExecuteAsync(Context("speakers"));

		Assert.Multiple(() =>
		{
			Assert.That(set.ErrorCode, Is.EqualTo(ActionErrorCodes.Unavailable));
			Assert.That(mute.ErrorCode, Is.EqualTo(ActionErrorCodes.Unavailable));
			Assert.That(volume.Volume, Is.EqualTo(0.5f));
			Assert.That(volume.Muted, Is.False);
		});
	}

	[Test]
	public async Task The_device_picker_offers_both_defaults_every_device_and_an_absent_stored_device()
	{
		var volume = new FakeVolumeService();
		volume.Devices.Add(new AudioDevice("headset-uid", "Arctis 7", AudioFlow.Output, false));
		volume.Devices.Add(new AudioDevice(_microphone.DeviceId!, "USB Mic", AudioFlow.Input, true));
		var gone = new AudioTarget(AudioFlow.Output, "unplugged-uid");
		var action = new MuteVolumeActionDefinition(volume, target => target == gone ? "Old DAC" : null);

		var result = await action.GetDynamicOptionsAsync(new DynamicOptionsContext
			{
				ParameterName = "device",
				CurrentParameters = new Dictionary<string, object?> { ["device"] = gone.ToParameterValue() }
			},
			CancellationToken.None);

		Assert.That(result.Options.Select(option => option.Value),
			Is.EquivalentTo(new[]
			{
				"default-output", "default-input", _headset.ToParameterValue(), _microphone.ToParameterValue(),
				gone.ToParameterValue()
			}));
	}

	[Test]
	public async Task Two_devices_with_the_same_name_get_distinguishable_labels()
	{
		var volume = new FakeVolumeService();
		volume.Devices.Add(new AudioDevice("first-phone", "iPhone Microphone", AudioFlow.Input, false));
		volume.Devices.Add(new AudioDevice("second-phone", "iPhone Microphone", AudioFlow.Input, false));
		volume.Devices.Add(new AudioDevice("speakers", "Speakers", AudioFlow.Output, true));

		var result = await new MuteVolumeActionDefinition(volume).GetDynamicOptionsAsync(
			new DynamicOptionsContext { ParameterName = "device", CurrentParameters = new Dictionary<string, object?>() },
			CancellationToken.None);

		var devices = result.Options
			.Where(option => option.Value.StartsWith("input:", StringComparison.Ordinal) ||
				option.Value.StartsWith("output:", StringComparison.Ordinal))
			.ToDictionary(option => option.Value, option => option.Label.Localized!.Value.Arguments["device"]?.ToString());
		Assert.Multiple(() =>
		{
			Assert.That(devices["input:first-phone"], Is.Not.EqualTo(devices["input:second-phone"]));
			Assert.That(devices["input:first-phone"], Does.StartWith("iPhone Microphone #"));
			Assert.That(devices["output:speakers"], Is.EqualTo("Speakers"));
		});
	}

	[Test]
	public void Every_volume_action_offers_the_device_parameter()
	{
		var volume = new FakeVolumeService();
		IActionDefinition[] actions =
		[
			new IncreaseVolumeActionDefinition(volume), new DecreaseVolumeActionDefinition(volume),
			new SetVolumeActionDefinition(volume), new MuteVolumeActionDefinition(volume)
		];

		Assert.That(actions,
			Has.All.Matches<IActionDefinition>(action =>
				action.Parameters.Any(parameter => parameter.Name == "device" && !parameter.Required)));
	}
}
