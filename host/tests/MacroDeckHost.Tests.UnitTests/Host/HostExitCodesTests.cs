using System.Security.Cryptography;

namespace MacroDeckHost.Tests.UnitTests.Host;

public class HostExitCodesTests
{
	[Test]
	public void The_restart_exit_code_is_the_one_the_shell_expects()
	{
		Assert.That(HostExitCodes.RestartRequested, Is.EqualTo(86));
	}

	[Test]
	public void The_cryptography_unavailable_exit_code_is_the_one_the_shell_expects()
	{
		Assert.That(HostExitCodes.CryptographyUnavailable, Is.EqualTo(87));
	}

	[TestCase(unchecked((int)0x800706D9))]
	[TestCase(unchecked((int)0x800706BA))]
	[TestCase(1753)]
	[TestCase(1722)]
	public void An_unreachable_key_isolation_service_is_reported_with_its_own_exit_code(int hresult)
	{
		var cause = new CryptographicException("no endpoints") { HResult = hresult };

		Assert.Multiple(() =>
		{
			Assert.That(HostExitCodes.ForStartupFailure(cause), Is.EqualTo(HostExitCodes.CryptographyUnavailable));
			Assert.That(HostExitCodes.ForStartupFailure(new TypeInitializationException("Licensing", cause)),
				Is.EqualTo(HostExitCodes.CryptographyUnavailable));
			Assert.That(HostExitCodes.ForStartupFailure(new AggregateException(new InvalidOperationException(),
					new InvalidOperationException("startup", cause))),
				Is.EqualTo(HostExitCodes.CryptographyUnavailable));
		});
	}

	[Test]
	public void Any_other_startup_failure_keeps_the_generic_exit_code()
	{
		Assert.Multiple(() =>
		{
			Assert.That(HostExitCodes.ForStartupFailure(new CryptographicException("bad padding")),
				Is.EqualTo(HostExitCodes.Failed));
			Assert.That(HostExitCodes.ForStartupFailure(new InvalidOperationException("boom")),
				Is.EqualTo(HostExitCodes.Failed));
		});
	}
}
