using System.Runtime.Versioning;
using MacroDeckHost.Integrations.System.Volume;

namespace MacroDeckHost.Tests.UnitTests.Linux.System;

[Platform("Linux")]
[SupportedOSPlatform("linux")]
public class VolumeServiceLinuxTests
{
	[Test]
	public async Task A_sink_that_does_not_exist_reads_as_unavailable_rather_than_unmuted()
	{
		var service = VolumeServiceFactory.Create();
		var target = new AudioTarget(AudioFlow.Output, "macro_deck_test.no_such_sink");

		Assert.Multiple(async () =>
		{
			Assert.That(await service.GetMuteAsync(target), Is.Null);
			Assert.That(await service.GetVolumeAsync(target), Is.Null);
			Assert.That(await service.SetMuteAsync(target, true), Is.False);
		});
	}

	[Test]
	public void Listing_devices_does_not_throw_without_an_audio_server()
	{
		var service = VolumeServiceFactory.Create();

		Assert.DoesNotThrowAsync(() => service.GetDevicesAsync());
	}
}
