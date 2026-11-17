using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Serilog;

namespace MacroDeckHost.Platform.Native;

[SupportedOSPlatform("windows")]
internal static class Win32Input
{
	public const uint InputMouse = 0;
	public const uint InputKeyboard = 1;

	private const long BlockedReportIntervalMs = 30_000;

	private static long _lastBlockedReportTicks = long.MinValue;

	public static void Send(IReadOnlyCollection<Input> inputs)
	{
		if (inputs.Count == 0)
		{
			return;
		}

		var array = inputs.ToArray();
		var sent = SendInput((uint)array.Length, array, Marshal.SizeOf<Input>());
		var error = Marshal.GetLastWin32Error();
		if (sent < array.Length)
		{
			ReportBlockedInput(array.Length - (int)sent, array.Length, error);
			return;
		}

		// UIPI discards keyboard input to a higher-integrity foreground window without shortening the count
		// or setting an error. Mouse input goes to the window under its own point instead, so it is not judged.
		if (array.All(input => input.type == InputKeyboard))
		{
			ReportIfForegroundOutranks();
		}
	}

	public static void ReportBlockedByIntegrity(uint processId)
	{
		if (!TryClaimReport())
		{
			return;
		}

		var path = Win32Windows.TryQueryFullProcessImagePath(processId);
		var application = path is null ? processId.ToString(CultureInfo.InvariantCulture) : Path.GetFileName(path);
		Log.ForContext(typeof(Win32Input))
			.Warning("Windows may block input to {Application}: it may be running with higher privileges than "
				+ "Macro Deck, for example elevated as administrator",
				application);
	}

	private static void ReportIfForegroundOutranks()
	{
		_ = Win32Windows.GetWindowThreadProcessId(Win32Windows.GetForegroundWindow(), out var processId);
		if (processId == 0)
		{
			return;
		}

		if (Win32Integrity.Outranks(Win32Integrity.Read(processId), Win32Integrity.Current))
		{
			ReportBlockedByIntegrity(processId);
		}
	}

	// A blocked target stays blocked for as long as it is focused, and input is sent per key or movement,
	// hence the throttle.
	private static bool TryClaimReport()
	{
		var now = Environment.TickCount64;
		if (now - Interlocked.Read(ref _lastBlockedReportTicks) < BlockedReportIntervalMs)
		{
			return false;
		}

		Interlocked.Exchange(ref _lastBlockedReportTicks, now);
		return true;
	}

	private static void ReportBlockedInput(int blocked, int total, int error)
	{
		if (!TryClaimReport())
		{
			return;
		}

		Log.ForContext(typeof(Win32Input))
			.Warning("Windows blocked {Blocked} of {Total} injected input event(s) (error {Error}); "
				+ "the foreground application may be running elevated",
				blocked,
				total,
				error);
	}

	[DllImport("user32.dll", SetLastError = true)]
	private static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);

	[StructLayout(LayoutKind.Sequential)]
	internal struct Input
	{
		public uint type;
		public InputUnion union;
	}

	[StructLayout(LayoutKind.Explicit)]
	internal struct InputUnion
	{
		[FieldOffset(0)]
		public Mouseinput mouse;

		[FieldOffset(0)]
		public Keybdinput keyboard;
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct Keybdinput
	{
		public ushort wVk;
		public ushort wScan;
		public uint dwFlags;
		public uint time;
		public IntPtr dwExtraInfo;
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct Mouseinput
	{
		public int dx;
		public int dy;
		public uint mouseData;
		public uint dwFlags;
		public uint time;
		public IntPtr dwExtraInfo;
	}
}
