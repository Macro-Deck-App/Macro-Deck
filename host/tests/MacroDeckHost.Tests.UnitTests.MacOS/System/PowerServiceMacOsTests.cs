using System.Runtime.Versioning;
using MacroDeckHost.Integrations.System.Power;

namespace MacroDeckHost.Tests.UnitTests.MacOS.System;

[Platform("MacOsX")]
[SupportedOSPlatform("macos")]
public class PowerServiceMacOsTests
{
	[Test]
	public void Factory_returns_the_macos_service()
	{
		var power = PowerServiceFactory.Create();

		Assert.Multiple(() =>
		{
			Assert.That(power, Is.InstanceOf<MacOsPowerService>());
			Assert.That(power.IsSupported, Is.True);
		});
	}

	[Test]
	public void Hibernate_is_never_supported()
	{
		var power = PowerServiceFactory.Create();

		Assert.That(power.Supports(PowerOperation.Hibernate), Is.False);
	}

	[Test]
	public void Sleep_restart_and_shut_down_are_supported()
	{
		var power = PowerServiceFactory.Create();

		Assert.Multiple(() =>
		{
			Assert.That(power.Supports(PowerOperation.Sleep), Is.True);
			Assert.That(power.Supports(PowerOperation.Restart), Is.True);
			Assert.That(power.Supports(PowerOperation.ShutDown), Is.True);
		});
	}

	[Test]
	public void Lock_support_reflects_whether_cgsession_exists()
	{
		var power = PowerServiceFactory.Create();

		Assert.That(power.Supports(PowerOperation.Lock),
			Is.EqualTo(File.Exists(MacOsPowerCommandResolver.CgSessionPath)));
	}
}
