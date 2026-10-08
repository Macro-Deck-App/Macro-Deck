using System.Runtime.Versioning;
using MacroDeckHost.Infrastructure.Lifecycle;

namespace MacroDeckHost.Tests.UnitTests.Windows.Lifecycle;

[Platform("Win")]
[SupportedOSPlatform("windows")]
public class UserSessionEndWindowsTests
{
	[Test]
	public void A_running_session_is_not_reported_as_ending()
	{
		Assert.That(UserSessionEndFactory.Create().IsEnding, Is.False);
	}
}
