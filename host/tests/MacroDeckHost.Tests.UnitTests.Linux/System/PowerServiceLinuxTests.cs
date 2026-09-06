using System.Runtime.Versioning;
using MacroDeckHost.Integrations.System.Power;

namespace MacroDeckHost.Tests.UnitTests.Linux.System;

[Platform("Linux")]
[SupportedOSPlatform("linux")]
public class PowerServiceLinuxTests
{
	[Test]
	public void Factory_returns_the_linux_service()
	{
		var power = PowerServiceFactory.Create();

		Assert.That(power, Is.InstanceOf<LinuxPowerService>());
	}

	[Test]
	public void Supports_does_not_throw_for_any_operation()
	{
		var power = PowerServiceFactory.Create();

		Assert.Multiple(() =>
		{
			foreach (var operation in Enum.GetValues<PowerOperation>())
			{
				Assert.DoesNotThrow(() => power.Supports(operation), operation.ToString());
			}
		});
	}

	[Test]
	public void Is_supported_matches_whether_any_operation_is_supported()
	{
		var power = PowerServiceFactory.Create();

		var anySupported = Enum.GetValues<PowerOperation>().Any(power.Supports);

		if (!power.IsSupported)
		{
			Assert.That(anySupported, Is.False);
		}
	}
}
