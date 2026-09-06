namespace MacroDeckHost.Tests.UnitTests.Host;

public class HostExitCodesTests
{
	[Test]
	public void The_restart_exit_code_is_the_one_the_shell_expects()
	{
		Assert.That(HostExitCodes.RestartRequested, Is.EqualTo(86));
	}
}
