using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MacroDeckHost.Platform.Notifications;
using Serilog.Core;

namespace MacroDeckHost.Tests.UnitTests.Windows.Notifications;

[Platform("Win")]
[SupportedOSPlatform("windows")]
public class WindowsNotificationServiceTests
{
	[Test]
	public async Task The_owner_window_is_destroyed_once_the_notification_is_removed()
	{
		var service = new WindowsNotificationService(Logger.None, TimeSpan.FromMilliseconds(200));

		var window = await service.ShowCoreAsync("Macro Deck test", "Notification window lifetime");
		if (window == IntPtr.Zero)
		{
			Assert.Inconclusive("No notification area is available to show a notification");
		}

		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
		while (IsWindow(window) && DateTime.UtcNow < deadline)
		{
			await Task.Delay(50);
		}

		Assert.That(IsWindow(window), Is.False);
	}

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool IsWindow(IntPtr hWnd);
}
