using MacroDeck.Sdk.Actions;

namespace MacroDeck.Sdk.Tests.UnitTests.Actions;

[TestFixture]
public class HostOptionsSourcesTests
{
	[Test]
	public void The_option_source_ids_keep_the_values_hosts_already_answer()
	{
		Assert.Multiple(() =>
		{
			Assert.That(HostOptionsSources.Variables, Is.EqualTo("macrodeck.variables"));
			Assert.That(HostOptionsSources.UserVariables, Is.EqualTo("macrodeck.user-variables"));
			Assert.That(HostOptionsSources.Devices, Is.EqualTo("macrodeck.devices"));
			Assert.That(HostOptionsSources.AndroidDevices, Is.EqualTo("macrodeck.adb-devices"));
		});
	}
}
