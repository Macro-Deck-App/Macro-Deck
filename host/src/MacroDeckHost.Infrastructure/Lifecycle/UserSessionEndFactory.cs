using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MacroDeckHost.Application.Lifecycle;

namespace MacroDeckHost.Infrastructure.Lifecycle;

public static class UserSessionEndFactory
{
	private const int SmShuttingDown = 0x2000;

	public static UserSessionEnd Create()
		=> OperatingSystem.IsWindows() ? new UserSessionEnd(IsWindowsSessionShuttingDown) : new UserSessionEnd();

	[SupportedOSPlatform("windows")]
	private static bool IsWindowsSessionShuttingDown() => GetSystemMetrics(SmShuttingDown) != 0;

	[DllImport("user32.dll")]
	private static extern int GetSystemMetrics(int index);
}
