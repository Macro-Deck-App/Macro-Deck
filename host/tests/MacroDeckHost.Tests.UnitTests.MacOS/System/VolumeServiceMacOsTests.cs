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
}
