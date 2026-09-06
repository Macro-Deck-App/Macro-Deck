using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Serilog;

namespace MacroDeckHost.Integrations.Native;

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
		}
	}

	// UIPI blocks injection into a higher-integrity foreground window: the call succeeds from our side but
	// nothing reaches the target, so the loss is only visible through this log. It stays blocked for as long
	// as that window is focused, and mouse input is sent per movement, hence the throttle.
	private static void ReportBlockedInput(int blocked, int total, int error)
	{
		var now = Environment.TickCount64;
		if (now - Interlocked.Read(ref _lastBlockedReportTicks) < BlockedReportIntervalMs)
		{
			return;
		}

		Interlocked.Exchange(ref _lastBlockedReportTicks, now);
		Log.ForContext(typeof(Win32Input))
			.Warning("Windows blocked {Blocked} of {Total} injected input event(s) (error {Error}); " +
				"the foreground application may be running elevated",
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
