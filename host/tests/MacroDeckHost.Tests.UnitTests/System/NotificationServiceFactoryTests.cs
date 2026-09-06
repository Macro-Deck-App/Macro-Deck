using MacroDeckHost.Integrations.System.Notifications;

namespace MacroDeckHost.Tests.UnitTests.System;

// The bridge the factory reaches is process-wide, because the built-in system integration is created
// by reflection and never sees the container - so this fixture owns it for the duration of its tests.
[NonParallelizable]
public class NotificationServiceFactoryTests
{
	[Test]
	public async Task Notifications_raised_anywhere_in_the_host_go_to_the_shell_when_one_is_attached()
	{
		var bridge = ShellNotificationBridge.Instance;
		using var abandoned = new CancellationTokenSource();
		await abandoned.CancelAsync();
		await bridge.WaitAsync(TimeSpan.FromSeconds(20), abandoned.Token);

		var show = NotificationServiceFactory.Create().ShowAsync("A plugin wants to pair", "Example Plugin");
		var polled = await bridge.WaitAsync(TimeSpan.FromSeconds(20));

		Assert.That(polled, Has.Count.EqualTo(1));
		Assert.That(polled[0].Title, Is.EqualTo("A plugin wants to pair"));
		Assert.That(polled[0].Message, Is.EqualTo("Example Plugin"));

		bridge.Report(polled[0].Id, shown: true);
		await show;
	}
}
