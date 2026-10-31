using MacroDeckHost.Platform.Native;

namespace MacroDeckHost.Tests.UnitTests.System;

public class LinuxDisplaySessionTests
{
	[TestCase("wayland", null, true)]
	[TestCase("WAYLAND", null, true)]
	[TestCase(null, "wayland-0", true)]
	[TestCase("x11", "wayland-0", true)]
	[TestCase("x11", null, false)]
	[TestCase("tty", "", false)]
	[TestCase(null, null, false)]
	public void Detects_a_wayland_session(string? sessionType, string? waylandDisplay, bool expected)
	{
		var environment = new Dictionary<string, string?>
		{
			["XDG_SESSION_TYPE"] = sessionType,
			["WAYLAND_DISPLAY"] = waylandDisplay
		};

		Assert.That(LinuxDisplaySession.DetectWayland(name => environment[name]), Is.EqualTo(expected));
	}
}
