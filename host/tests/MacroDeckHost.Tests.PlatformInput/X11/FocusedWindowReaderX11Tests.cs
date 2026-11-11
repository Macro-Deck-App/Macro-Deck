using System.Runtime.Versioning;
using MacroDeckHost.Platform.Focus;
using MacroDeckHost.Tests.Support;

namespace MacroDeckHost.Tests.PlatformInput.X11;

[Platform("Linux")]
[Category("X11")]
[SupportedOSPlatform("linux")]
public sealed class FocusedWindowReaderX11Tests
{
	[Test]
	public void The_focused_window_is_read_from_the_window_manager()
	{
		using var probe = ProbeProcess.StartWindow("mdprobe-focus");
		ProbeFocus.Focus(probe);
		using var reader = new LinuxFocusedWindowReader();

		Assert.That(reader.IsSupported, Is.True, reader.UnsupportedReason);
		Assert.That(() => reader.Read()?.ProcessId, Is.EqualTo(probe.Pid).After(5000, 50));
	}
}
