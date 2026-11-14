using MacroDeckHost.Platform.Native;

namespace MacroDeckHost.Tests.Support;

internal static class ProbeFocus
{
	public static void Focus(ProbeProcess probe)
	{
		if (probe.IsFocused)
		{
			return;
		}

		if (OperatingSystem.IsWindows())
		{
			Interactive.Require(Win32Windows.GetForegroundWindow() != IntPtr.Zero,
				"No foreground window in this session (no input desktop).");
			Assert.That(Win32Foreground.TryActivate((nint)probe.Ready.Window!.Value), Is.True, "activate the probe");
		}
		else
		{
			probe.Send("focus");
		}

		Assert.That(() => probe.IsFocused,
			Is.True.After(5000, 20),
			$"{probe.Name} gets the focus{Environment.NewLine}{probe.Diagnostics}");
	}
}
