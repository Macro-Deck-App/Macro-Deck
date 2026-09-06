using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Serilog;

namespace MacroDeckHost.Integrations.Native;

internal static class MacOsMainRunLoop
{
	private const string CoreFoundation = MacOsAccessibility.CoreFoundation;

	// CFRunLoopRunResult.kCFRunLoopRunFinished - the mode has no registered sources or timers left.
	private const int RunFinished = 1;

	private static readonly TimeSpan _sliceDuration = TimeSpan.FromSeconds(0.25);

	private static volatile bool _isPumping;

	public static bool IsPumping => _isPumping;

	public static void MarkPumping() => _isPumping = true;

	[SupportedOSPlatform("macos")]
	public static void PumpUntil(Task completion)
	{
		var mode = MacOsAccessibility.ReadGlobalRef(CoreFoundation, "kCFRunLoopDefaultMode");
		if (mode == IntPtr.Zero)
		{
			// The claim made before the host task started is now known to be false. Retracting it lets
			// the watcher factory choose polling instead of a native watcher that nothing would pump.
			_isPumping = false;
			Log.Warning("The macOS main run loop could not start; application focus falls back to polling");
			return;
		}

		while (!completion.IsCompleted)
		{
			var result = CFRunLoopRunInMode(mode, _sliceDuration.TotalSeconds, false);
			if (result == RunFinished)
			{
				// Nothing registers a source that would make CFRunLoopRunInMode return "finished" in
				// this mode today, but a future CoreFoundation behaviour change here must not turn this
				// into a busy loop, so the rest of the slice is slept out instead of looping immediately.
				Thread.Sleep(_sliceDuration);
			}
		}
	}

	[DllImport(CoreFoundation)]
	private static extern int CFRunLoopRunInMode(IntPtr mode,
		double seconds,
		[MarshalAs(UnmanagedType.I1)] bool returnAfterSourceHandled);
}
