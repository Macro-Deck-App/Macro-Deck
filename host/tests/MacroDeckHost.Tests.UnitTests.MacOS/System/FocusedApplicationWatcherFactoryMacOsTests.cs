using System.Runtime.Versioning;
using MacroDeckHost.Integrations.System.Focus;

namespace MacroDeckHost.Tests.UnitTests.MacOS.System;

[Platform("MacOsX")]
[SupportedOSPlatform("macos")]
public class FocusedApplicationWatcherFactoryMacOsTests
{
	[Test]
	public void Create_returns_the_polling_watcher_when_the_run_loop_is_not_pumping()
	{
		// A unit-test host never pumps a CoreFoundation run loop (only MacroDeckHost.Program does, on
		// its process main thread), so this is the naturally reachable branch here.
		using var watcher = FocusedApplicationWatcherFactory.Create();

		Assert.Multiple(() =>
		{
			Assert.That(watcher, Is.InstanceOf<PollingFocusedApplicationWatcher>());
			Assert.That(watcher.IsSupported, Is.True);
			Assert.That(watcher.UnsupportedReason, Is.Null);
		});
	}
}
