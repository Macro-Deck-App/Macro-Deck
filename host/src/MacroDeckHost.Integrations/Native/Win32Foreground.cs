using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MacroDeckHost.Integrations.Native;

[SupportedOSPlatform("windows")]
internal static class Win32Foreground
{
	private const int SwRestore = 9;
	private const uint SpiGetForegroundLockTimeout = 0x2000;
	private const uint SpiSetForegroundLockTimeout = 0x2001;
	private const int ActivationTimeoutMs = 500;
	private const int PollIntervalMs = 10;

	private static readonly Lock _lockTimeoutGate = new();

	// Windows only lets the process that owns the foreground window (or received the last input) call
	// SetForegroundWindow; every other caller gets its request downgraded to a taskbar flash. Sharing an
	// input queue with the current foreground thread via AttachThreadInput is what lifts that restriction
	// for the duration of the call. It still fails against a higher-integrity (elevated) target, which is
	// reported as a failure rather than silently ignored.
	public static bool TryActivate(nint window) => TryActivate(window, ActivationTimeoutMs);

	/// <param name="verifyTimeoutMs">
	/// How long to wait for the window to actually reach the foreground. Pass 0 to request activation
	/// without waiting for it, for callers that do not act on the result.
	/// </param>
	public static bool TryActivate(nint window, int verifyTimeoutMs)
	{
		if (window == nint.Zero)
		{
			return false;
		}

		var targetThread = Win32Windows.GetWindowThreadProcessId(window, out var targetProcess);
		if (targetThread == 0)
		{
			return false;
		}

		var currentThread = GetCurrentThreadId();
		var foregroundThread = ThreadOf(Win32Windows.GetForegroundWindow());

		var attachedToForeground = TryAttach(currentThread, foregroundThread);
		var attachedToTarget = TryAttach(currentThread, targetThread);
		try
		{
			lock (_lockTimeoutGate)
			{
				var lockTimeoutRead = TryGetForegroundLockTimeout(out var previousTimeout);
				if (lockTimeoutRead)
				{
					SetForegroundLockTimeout(0);
				}

				try
				{
					if (IsIconic(window))
					{
						_ = ShowWindow(window, SwRestore);
					}

					_ = BringWindowToTop(window);
					_ = SetForegroundWindow(window);
				}
				finally
				{
					if (lockTimeoutRead)
					{
						SetForegroundLockTimeout(previousTimeout);
					}
				}
			}
		}
		finally
		{
			Detach(currentThread, foregroundThread, attachedToForeground);
			Detach(currentThread, targetThread, attachedToTarget);
		}

		return WaitForForeground(window, targetProcess, verifyTimeoutMs);
	}

	private static bool WaitForForeground(nint window, uint targetProcess, int timeoutMs)
	{
		for (var elapsed = 0;; elapsed += PollIntervalMs)
		{
			if (IsForeground(window, targetProcess))
			{
				return true;
			}

			if (elapsed >= timeoutMs)
			{
				return false;
			}

			Thread.Sleep(PollIntervalMs);
		}
	}

	// An application often responds to activation by raising a different top-level window of its own, so
	// the requested handle no longer being the foreground one does not mean activation failed.
	private static bool IsForeground(nint window, uint targetProcess)
	{
		var foreground = Win32Windows.GetForegroundWindow();
		if (foreground == window)
		{
			return true;
		}

		if (foreground == nint.Zero || targetProcess == 0)
		{
			return false;
		}

		_ = Win32Windows.GetWindowThreadProcessId(foreground, out var foregroundProcess);
		return foregroundProcess == targetProcess;
	}

	private static uint ThreadOf(nint window)
		=> window == nint.Zero ? 0 : Win32Windows.GetWindowThreadProcessId(window, out _);

	private static bool TryAttach(uint currentThread, uint otherThread)
		=> otherThread != 0 && otherThread != currentThread && AttachThreadInput(currentThread, otherThread, true);

	private static void Detach(uint currentThread, uint otherThread, bool attached)
	{
		if (attached)
		{
			_ = AttachThreadInput(currentThread, otherThread, false);
		}
	}

	private static bool TryGetForegroundLockTimeout(out uint timeout)
	{
		timeout = 0;
		return SystemParametersInfo(SpiGetForegroundLockTimeout, 0, ref timeout, 0);
	}

	// fWinIni stays 0 so the user's persisted setting is never rewritten - the change lasts for this
	// session only and is restored immediately.
	private static void SetForegroundLockTimeout(uint timeout)
		=> _ = SystemParametersInfoSet(SpiSetForegroundLockTimeout, 0, (nint)timeout, 0);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool SetForegroundWindow(nint hWnd);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool BringWindowToTop(nint hWnd);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool ShowWindow(nint hWnd, int nCmdShow);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool IsIconic(nint hWnd);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool AttachThreadInput(
		uint idAttach,
		uint idAttachTo,
		[MarshalAs(UnmanagedType.Bool)] bool attach);

	[DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool SystemParametersInfo(uint action, uint param, ref uint value, uint winIni);

	[DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool SystemParametersInfoSet(uint action, uint param, nint value, uint winIni);

	[DllImport("kernel32.dll")]
	private static extern uint GetCurrentThreadId();
}
