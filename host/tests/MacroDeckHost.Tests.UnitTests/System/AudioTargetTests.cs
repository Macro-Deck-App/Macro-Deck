using MacroDeckHost.Integrations.System.Volume;

namespace MacroDeckHost.Tests.UnitTests.System;

public class AudioTargetTests
{
	[TestCase(null)]
	[TestCase("")]
	[TestCase("default-output")]
	public void A_missing_or_default_value_targets_the_default_output(string? value)
	{
		var parsed = AudioTarget.TryParse(value, out var target);

		Assert.Multiple(() =>
		{
			Assert.That(parsed, Is.True);
			Assert.That(target, Is.EqualTo(AudioTarget.DefaultOutput));
		});
	}

	[Test]
	public void The_default_input_round_trips()
	{
		var parsed = AudioTarget.TryParse(AudioTarget.DefaultInput.ToParameterValue(), out var target);

		Assert.Multiple(() =>
		{
			Assert.That(parsed, Is.True);
			Assert.That(target, Is.EqualTo(AudioTarget.DefaultInput));
		});
	}

	[Test]
	public void A_device_id_containing_colons_round_trips()
	{
		var device = new AudioTarget(AudioFlow.Input, "AppleUSBAudioEngine:Vendor:Headset:1400000:2");

		var parsed = AudioTarget.TryParse(device.ToParameterValue(), out var target);

		Assert.Multiple(() =>
		{
			Assert.That(parsed, Is.True);
			Assert.That(target, Is.EqualTo(device));
		});
	}

	[TestCase("speakers")]
	[TestCase("output:")]
	[TestCase("line:headset")]
	public void An_unrecognised_value_is_refused(string value)
	{
		Assert.That(AudioTarget.TryParse(value, out _), Is.False);
	}
}
