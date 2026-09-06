using System.Runtime.Versioning;
using MacroDeckHost.Integrations.System.Lock;

namespace MacroDeckHost.Tests.UnitTests.MacOS.System;

[Platform("MacOsX")]
[SupportedOSPlatform("macos")]
public class LockStateReaderMacOsTests
{
	[Test]
	public void Factory_returns_a_supported_reader()
	{
		var reader = LockStateReaderFactory.Create();

		Assert.Multiple(() =>
		{
			Assert.That(reader, Is.InstanceOf<MacOsLockStateReader>());
			Assert.That(reader.IsSupported, Is.True);
			Assert.That(reader.UnsupportedReason, Is.Null);
		});
	}

	// On an attended session a reader that answers "locked" is wrong: the CGSSessionScreenIsLocked key
	// is absent while unlocked, and the reader used to report that absence as unknown, latching the
	// host at "locked" forever. That is what this guards, and it was found by running the host rather
	// than by this test - the complementary half, an unlocked GUI session answering false rather than
	// null, only reproduces on a real desktop.
	//
	// A test run is not itself proof of such a session, and nothing inside the process can establish
	// one: a CI VM can own /dev/console and still sit at a locked screen, where "locked" is the
	// truthful answer rather than the bug. So the assertion is made where a human is plausibly at the
	// machine and skipped where one demonstrably is not.
	[Test]
	public void IsLocked_never_reports_locked_while_the_tests_are_running()
	{
		var reader = LockStateReaderFactory.Create();

		bool? locked = null;
		Assert.DoesNotThrow(() => locked = reader.IsLocked());

		TestContext.Out.WriteLine($"IsLocked: {locked?.ToString() ?? "<unknown>"}");

		if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")))
		{
			Assert.Ignore("No attended desktop session on a CI runner, so a locked screen is truthful.");
		}

		Assert.That(locked, Is.Not.True);
	}
}
