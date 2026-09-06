using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MacroDeckHost.Integrations.Native;

namespace MacroDeckHost.Tests.UnitTests.Windows.Native;

[Platform("Win")]
[SupportedOSPlatform("windows")]
public class ForegroundActivationWindowsTests
{
	private const uint SpiGetForegroundLockTimeout = 0x2000;

	[Test]
	public void Activating_the_window_that_is_already_in_the_foreground_succeeds()
	{
		var foreground = Win32Windows.GetForegroundWindow();
		if (foreground == nint.Zero)
		{
			Assert.Ignore("No foreground window in this session (headless or locked desktop).");
		}

		Assert.That(Win32Foreground.TryActivate(foreground), Is.True);
	}

	[Test]
	public void Activating_a_window_that_does_not_exist_fails_without_throwing()
	{
		var activated = true;

		Assert.DoesNotThrow(() => activated = Win32Foreground.TryActivate(nint.Zero));
		Assert.That(activated, Is.False);
	}

	[Test]
	public void Activation_leaves_the_users_foreground_lock_timeout_unchanged()
	{
		var foreground = Win32Windows.GetForegroundWindow();
		if (foreground == nint.Zero)
		{
			Assert.Ignore("No foreground window in this session (headless or locked desktop).");
		}

		var before = ReadForegroundLockTimeout();

		_ = Win32Foreground.TryActivate(foreground);

		Assert.That(ReadForegroundLockTimeout(), Is.EqualTo(before));
	}

	private static uint ReadForegroundLockTimeout()
	{
		uint timeout = 0;
		return SystemParametersInfoW(SpiGetForegroundLockTimeout, 0, ref timeout, 0) ? timeout : uint.MaxValue;
	}

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool SystemParametersInfoW(uint action, uint param, ref uint value, uint winIni);
}
