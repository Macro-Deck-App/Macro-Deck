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
		Assert.DoesNotThrowAsync(async () => volume = await service.GetVolumeAsync());
		Assert.DoesNotThrowAsync(async () => muted = await service.GetMuteAsync());

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
}
