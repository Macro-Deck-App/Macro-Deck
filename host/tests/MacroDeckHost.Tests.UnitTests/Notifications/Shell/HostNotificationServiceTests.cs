using MacroDeckHost.Infrastructure.Notifications.Shell;
using MacroDeckHost.Platform.Notifications;
using MacroDeck.Sdk.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.Notifications.Shell;

public class HostNotificationServiceTests
{
	[Test]
	public async Task Notifications_raised_anywhere_in_the_host_go_to_the_shell_when_one_is_attached()
	{
		await using var provider = NotificationWiring();
		var bridge = provider.GetRequiredService<IShellNotificationBridge>();
		var notifications = provider.GetRequiredService<IPlatformNotificationService>();
		using var abandoned = new CancellationTokenSource();
		await abandoned.CancelAsync();
		await bridge.WaitAsync(TimeSpan.FromSeconds(20), abandoned.Token);

		var show = notifications.ShowAsync("A plugin wants to pair", "Example Plugin");
		var polled = await bridge.WaitAsync(TimeSpan.FromSeconds(20));

		Assert.That(polled, Has.Count.EqualTo(1));
		Assert.That(polled[0].Title, Is.EqualTo("A plugin wants to pair"));
		Assert.That(polled[0].Message, Is.EqualTo("Example Plugin"));

		bridge.Report(polled[0].Id, true);
		await show;
	}

	[Test]
	public async Task Desktop_notifications_integrations_show_go_to_the_shell_when_one_is_attached()
	{
		await using var provider = NotificationWiring();
		var bridge = provider.GetRequiredService<IShellNotificationBridge>();
		var desktop = provider.GetRequiredService<IDesktopNotifier>();
		using var abandoned = new CancellationTokenSource();
		await abandoned.CancelAsync();
		await bridge.WaitAsync(TimeSpan.FromSeconds(20), abandoned.Token);

		var show = desktop.ShowAsync("Build finished", "All green");
		var polled = await bridge.WaitAsync(TimeSpan.FromSeconds(20));

		Assert.Multiple(() =>
		{
			Assert.That(desktop.IsSupported, Is.True);
			Assert.That(polled, Has.Count.EqualTo(1));
			Assert.That((polled[0].Title, polled[0].Message), Is.EqualTo(("Build finished", "All green")));
		});

		bridge.Report(polled[0].Id, true);
		await show;
	}

	private static ServiceProvider NotificationWiring()
	{
		var startup = new ServiceCollection();
		new Startup().ConfigureServices(startup);

		IServiceCollection wiring = new ServiceCollection();
		foreach (var descriptor in startup.Where(descriptor =>
			descriptor.ServiceType == typeof(IShellNotificationBridge)
			|| descriptor.ServiceType == typeof(IPlatformNotificationService)
			|| descriptor.ServiceType == typeof(IDesktopNotifier)))
		{
			wiring.Add(descriptor);
		}

		return wiring.BuildServiceProvider();
	}
}
