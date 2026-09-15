using MacroDeckHost.Integrations.System.Volume;

namespace MacroDeckHost.Tests.UnitTests.Windows.System;

[Platform("Win")]
public class VolumeServiceWindowsTests
{
	[Test]
	public void Reading_the_volume_reports_a_level_or_an_absent_endpoint()
	{
		var service = VolumeServiceFactory.Create();
		float? volume = null;
		bool? muted = null;

		Assert.That(service.IsSupported, Is.True);
		Assert.DoesNotThrowAsync(async () => volume = await service.GetVolumeAsync(AudioTarget.DefaultOutput));
		Assert.DoesNotThrowAsync(async () => muted = await service.GetMuteAsync(AudioTarget.DefaultOutput));

		TestContext.Out.WriteLine(volume is null && muted is null
			? "No default audio endpoint."
			: FormattableString.Invariant($"Volume: {volume}, muted: {muted}"));

		Assert.Multiple(() =>
		{
			if (volume is not null)
			{
				Assert.That(volume.Value, Is.InRange(0f, 1f));
			}

			Assert.That(muted is null,
				Is.EqualTo(volume is null),
				"Both readings come from the same default endpoint, so they are available together.");
		});
	}

	[Test]
	public void Subscribing_to_and_unsubscribing_from_volume_changes_does_not_throw()
	{
		var service = VolumeServiceFactory.Create();
		Action handler = () => { };

		Assert.Multiple(() =>
		{
			Assert.DoesNotThrow(() => service.Changed += handler);
			Assert.DoesNotThrowAsync(() => service.GetVolumeAsync(AudioTarget.DefaultOutput));
			Assert.DoesNotThrow(() => service.Changed -= handler);
		});
	}

	[Test]
	public async Task Every_listed_device_has_an_id_and_a_name_and_can_be_read()
	{
		var service = VolumeServiceFactory.Create();

		var devices = await service.GetDevicesAsync();
		TestContext.Out.WriteLine(FormattableString.Invariant($"{devices.Count} active endpoint(s)."));

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
	public async Task A_device_id_that_does_not_exist_reads_as_unavailable()
	{
		var service = VolumeServiceFactory.Create();
		var target = new AudioTarget(AudioFlow.Output, "{0.0.0.00000000}.{00000000-0000-0000-0000-000000000000}");

		Assert.Multiple(async () =>
		{
			Assert.That(await service.GetVolumeAsync(target), Is.Null);
			Assert.That(await service.SetVolumeAsync(target, 0.5f), Is.False);
		});
	}

	[Test]
	public async Task Reading_the_microphone_keeps_the_default_output_listener_in_place()
	{
		var service = VolumeServiceFactory.Create();
		var reading = await service.GetVolumeAsync(AudioTarget.DefaultOutput);
		if (reading is null)
		{
			Assert.Ignore("No default output endpoint on this machine.");
			return;
		}

		var original = reading.Value;

		var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		Action handler = () => changed.TrySetResult();
		service.Changed += handler;
		try
		{
			await service.GetVolumeAsync(AudioTarget.DefaultInput);
			await service.SetVolumeAsync(AudioTarget.DefaultOutput, original > 0.5f ? original - 0.01f : original + 0.01f);

			Assert.That(async () => await changed.Task.WaitAsync(TimeSpan.FromSeconds(5)), Throws.Nothing);
		}
		finally
		{
			service.Changed -= handler;
			await service.SetVolumeAsync(AudioTarget.DefaultOutput, original);
		}
	}
}
