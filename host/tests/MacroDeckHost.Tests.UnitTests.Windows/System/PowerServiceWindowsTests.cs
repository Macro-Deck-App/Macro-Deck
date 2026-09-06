using System.Runtime.Versioning;
using MacroDeckHost.Integrations.System.Power;

namespace MacroDeckHost.Tests.UnitTests.Windows.System;

[Platform("Win")]
[SupportedOSPlatform("windows")]
public class PowerServiceWindowsTests
{
	[Test]
	public void Factory_returns_the_windows_service()
	{
		var power = PowerServiceFactory.Create();

		Assert.Multiple(() =>
		{
			Assert.That(power, Is.InstanceOf<WindowsPowerService>());
			Assert.That(power.IsSupported, Is.True);
		});
	}

	[Test]
	public void Supports_lock_sleep_restart_and_shut_down()
	{
		var power = PowerServiceFactory.Create();

		Assert.Multiple(() =>
		{
			Assert.That(power.Supports(PowerOperation.Lock), Is.True);
			Assert.That(power.Supports(PowerOperation.Sleep), Is.True);
			Assert.That(power.Supports(PowerOperation.Restart), Is.True);
			Assert.That(power.Supports(PowerOperation.ShutDown), Is.True);
		});
	}

	[Test]
	public void Supports_hibernate_marshals_the_power_capabilities_query_without_throwing()
	{
		var power = PowerServiceFactory.Create();

		Assert.DoesNotThrow(() => power.Supports(PowerOperation.Hibernate));
	}
}
