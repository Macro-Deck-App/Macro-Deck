namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

/// <summary>
/// <c>run</c>'s defaults: a bare <c>run</c> targets the real, already-running host in self-registering
/// mode, and the stub host is what has to be asked for. Every case here is decided before a process is
/// ever launched, so a missing <c>--executable</c> (exit code 3) is the marker for "validation let this
/// through" - the alternative would be launching a plugin from a unit test.
/// </summary>
[TestFixture]
public class RunHostDefaultsTests
{
	private static string MissingExecutable => Path.Combine(Path.GetTempPath(), "macrodeck-plugin-run-no-such-file");

	[Test]
	public async Task Managed_mode_is_refused_without_an_explicit_stub_host()
	{
		var (_, error, exitCode) = await CliRunner.Run("run", "--executable", MissingExecutable, "--mode", "managed");

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(2));
			Assert.That(error, Does.Contain("managed-needs-stub-host"));
		});
	}

	[Test]
	public async Task Pairing_off_without_a_token_is_refused_by_default()
	{
		// No --mode and no --host-url/--stub-host: only the self-registering default against a real host
		// can reach the token requirement at all - managed would have failed with a different code, and the
		// stub host mints its own placeholder token.
		var (_, error, exitCode)
			= await CliRunner.Run("run", "--executable", MissingExecutable, "--pairing", "false");

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(2));
			Assert.That(error, Does.Contain("enrollment-token-required"));
		});
	}

	[Test]
	public async Task The_stub_host_needs_no_credentials_of_its_own()
	{
		var (_, error, exitCode)
			= await CliRunner.Run("run", "--executable", MissingExecutable, "--stub-host", "--pairing", "false");

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(3));
			Assert.That(error, Does.Contain("executable-not-found"));
		});
	}

	[Test]
	public async Task Managed_mode_still_works_against_the_stub_host()
	{
		var (_, error, exitCode)
			= await CliRunner.Run("run", "--executable", MissingExecutable, "--stub-host", "--mode", "managed");

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(3));
			Assert.That(error, Does.Contain("executable-not-found"));
		});
	}
}
