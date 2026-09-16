using MacroDeckHost.Application.Variables;
using MacroDeckHost.Integrations.System;
using MacroDeckHost.Integrations.System.Volume;

namespace MacroDeckHost.Tests.UnitTests.System;

public class AudioDeviceVariablesTests
{
	private static List<string> NamesFor(params AudioDevice[] devices)
		=> AudioDeviceVariables.Merge([], devices)
			.SelectMany(AudioDeviceVariables.Declare)
			.Select(variable => variable.Name!)
			.ToList();

	[Test]
	public void A_device_name_starting_with_a_digit_gets_no_stray_prefix()
	{
		var names = NamesFor(new AudioDevice("usb", "2- USB Audio", AudioFlow.Output, false));

		Assert.That(names, Does.Contain("system_audio_output_2_usb_audio_volume_percent"));
	}

	[TestCase("V 2", "system_audio_input_v_2_muted")]
	[TestCase("Vive", "system_audio_input_vive_muted")]
	[TestCase("Écouteurs", "system_audio_input_ecouteurs_muted")]
	public void A_device_name_keeps_its_own_leading_letters(string deviceName, string expected)
	{
		var names = NamesFor(new AudioDevice("id", deviceName, AudioFlow.Input, false));

		Assert.That(names, Does.Contain(expected));
	}

	[Test]
	public void Long_and_colliding_device_names_still_fit_the_variable_name_limit()
	{
		const string longName = "An Extremely Long Professional Audio Interface Name That Never Ends";

		var names = NamesFor(new AudioDevice("first", longName, AudioFlow.Output, false),
			new AudioDevice("second", longName, AudioFlow.Output, false));

		Assert.Multiple(() =>
		{
			Assert.That(names, Has.Count.EqualTo(4));
			Assert.That(names.Distinct().Count(), Is.EqualTo(4));
			Assert.That(names, Has.All.Length.LessThanOrEqualTo(VariableNameFactory.MaxNameLength));
			Assert.That(names, Has.All.Matches<string>(VariableNameSanitizer.IsValid));
		});
	}

	[Test]
	public void A_full_list_forgets_the_oldest_absent_device_so_a_new_one_still_gets_variables()
	{
		var present = new AudioDevice("present", "Present", AudioFlow.Output, true);
		var known = AudioDeviceVariables.Merge([],
			[
				present,
				.. Enumerable.Range(0, AudioDeviceVariables.MaxKnownDevices - 1)
					.Select(i => new AudioDevice($"old-{i}", $"Old {i}", AudioFlow.Output, false))
			]);
		var newcomer = new AudioDevice("new", "New Headset", AudioFlow.Output, false);

		var merged = AudioDeviceVariables.Merge(known, [present, newcomer]);

		Assert.Multiple(() =>
		{
			Assert.That(merged, Has.Count.EqualTo(AudioDeviceVariables.MaxKnownDevices));
			Assert.That(merged.Select(d => d.DeviceId), Does.Contain("present").And.Contain("new"));
			Assert.That(merged.Select(d => d.DeviceId), Does.Not.Contain("old-0"));
			Assert.That(merged.Select(d => d.DeviceId), Does.Contain("old-1"));
		});
	}

	[Test]
	public void A_device_without_a_usable_name_is_named_after_its_key()
	{
		var names = NamesFor(new AudioDevice("id", "***", AudioFlow.Output, false));

		Assert.That(names, Has.All.Matches<string>(VariableNameSanitizer.IsValid));
	}
}
