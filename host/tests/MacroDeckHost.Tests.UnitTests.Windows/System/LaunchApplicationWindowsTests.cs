using MacroDeckHost.Integrations.System.Actions;
using MacroDeckHost.Tests.UnitTests.System;

namespace MacroDeckHost.Tests.UnitTests.Windows.System;

[Platform("Win")]
public class LaunchApplicationWindowsTests
{
	[Test]
	public void Exposes_run_as_admin_parameter_on_windows()
	{
		var action = new LaunchApplicationActionDefinition(new FakeApplicationService());

		Assert.That(action.Parameters.Any(p => p.Name == "runAsAdmin"), Is.True);
	}
}
