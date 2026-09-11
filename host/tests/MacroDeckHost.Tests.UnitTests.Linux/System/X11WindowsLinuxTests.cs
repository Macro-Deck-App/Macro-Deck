using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MacroDeckHost.Integrations.Native;

namespace MacroDeckHost.Tests.UnitTests.Linux.System;

[Platform("Linux")]
[SupportedOSPlatform("linux")]
public class X11WindowsLinuxTests
{
	private const string LibX11 = "libX11.so.6";

	[Test]
	public void Reading_a_window_that_was_just_closed_returns_nothing_instead_of_terminating_the_process()
	{
		var display = TryOpenDisplay();
		Assume.That(display, Is.Not.EqualTo(IntPtr.Zero), "needs libX11 and an X11 display, for example Xvfb");

		try
		{
			// A bare Xvfb has no window manager, so nothing has created the atom a real desktop always has.
			_ = XInternAtom(display, "_NET_WM_PID\0"u8.ToArray(), false);
			var window = XCreateSimpleWindow(display, XDefaultRootWindow(display), 0, 0, 10, 10, 0, 0, 0);
			_ = XDestroyWindow(display, window);
			_ = XSync(display, false);

			Assert.That(X11Windows.GetWindowPid(display, window), Is.Null);
		}
		finally
		{
			_ = XCloseDisplay(display);
		}
	}

	private static IntPtr TryOpenDisplay()
	{
		try
		{
			return X11Windows.OpenDisplay();
		}
		catch (DllNotFoundException)
		{
			return IntPtr.Zero;
		}
	}

	[DllImport(LibX11)]
	private static extern int XCloseDisplay(IntPtr display);

	[DllImport(LibX11)]
	private static extern nuint XInternAtom(
		IntPtr display,
		byte[] name,
		[MarshalAs(UnmanagedType.Bool)] bool onlyIfExists);

	[DllImport(LibX11)]
	private static extern nuint XDefaultRootWindow(IntPtr display);

	[DllImport(LibX11)]
	private static extern nuint XCreateSimpleWindow(
		IntPtr display,
		nuint parent,
		int x,
		int y,
		uint width,
		uint height,
		uint borderWidth,
		nuint border,
		nuint background);

	[DllImport(LibX11)]
	private static extern int XDestroyWindow(IntPtr display, nuint window);

	[DllImport(LibX11)]
	private static extern int XSync(IntPtr display, [MarshalAs(UnmanagedType.Bool)] bool discard);
}
