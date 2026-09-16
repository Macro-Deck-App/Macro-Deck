using MacroDeckHost.Integrations.System.Volume;

namespace MacroDeckHost.Tests.UnitTests.MacOS.System;

[Platform("MacOsX")]
public class VolumeServiceMacOsTests
{
	[Test]
	public void Subscribing_to_and_unsubscribing_from_volume_changes_does_not_throw()
	{
		var service = VolumeServiceFactory.Create();
		Action handler = () => { };

		Assert.Multiple(() =>
		{
			Assert.DoesNotThrow(() => service.Changed += handler);
			Assert.DoesNotThrow(() => service.Changed -= handler);
			Assert.DoesNotThrow(() => service.Changed += handler);
			Assert.DoesNotThrow(() => service.Changed -= handler);
		});
	}

	[Test]
	public async Task Every_listed_device_has_an_id_and_a_name_and_can_be_read()
	{
		var service = VolumeServiceFactory.Create();

		var devices = await service.GetDevicesAsync();
		TestContext.Out.WriteLine(string.Join(Environment.NewLine, devices));

		foreach (var device in devices)
		{
			var target = new AudioTarget(device.Flow, device.Id);
			Assert.Multiple(() =>
			{
				Assert.That(device.Id, Is.Not.Empty);
				Assert.That(device.Name, Is.Not.Empty);
				Assert.DoesNotThrowAsync(() => service.GetVolumeAsync(target));
				Assert.DoesNotThrowAsync(() => service.GetMuteAsync(target));
			});
		}
	}

	[Test]
	public async Task The_default_output_is_one_of_the_listed_devices()
	{
		var service = VolumeServiceFactory.Create();
		if (await service.GetVolumeAsync(AudioTarget.DefaultOutput) is null)
		{
			Assert.Ignore("No default output device with a volume control on this machine.");
		}

		var devices = await service.GetDevicesAsync();

		Assert.That(devices.Count(device => device is { Flow: AudioFlow.Output, IsDefault: true }), Is.EqualTo(1));
	}

	[Test]
	public async Task A_uid_that_does_not_exist_reads_as_unavailable()
	{
		var service = VolumeServiceFactory.Create();
		var target = new AudioTarget(AudioFlow.Output, "macro-deck-test:no-such-device");

		Assert.Multiple(async () =>
		{
			Assert.That(await service.GetVolumeAsync(target), Is.Null);
			Assert.That(await service.GetMuteAsync(target), Is.Null);
			Assert.That(await service.SetVolumeAsync(target, 0.5f), Is.False);
		});
	}
}
