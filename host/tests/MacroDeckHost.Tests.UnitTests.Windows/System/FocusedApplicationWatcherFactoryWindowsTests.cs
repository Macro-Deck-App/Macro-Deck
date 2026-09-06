using System.Runtime.Versioning;
using MacroDeckHost.Integrations.System.Focus;

namespace MacroDeckHost.Tests.UnitTests.Windows.System;

[Platform("Win")]
[SupportedOSPlatform("windows")]
public class FocusedApplicationWatcherFactoryWindowsTests
{
	[Test]
	public void Create_returns_the_native_watcher()
	{
		using var watcher = FocusedApplicationWatcherFactory.Create();

		Assert.Multiple(() =>
		{
			Assert.That(watcher, Is.Not.InstanceOf<PollingFocusedApplicationWatcher>());
			Assert.That(watcher.IsSupported, Is.True);
			Assert.That(watcher.UnsupportedReason, Is.Null);
		});
	}
}
