using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MacroDeckHost.Integrations.Keyboard.Native;
using MacroDeckHost.Platform.Native;

namespace MacroDeckHost.Tests.UnitTests.Windows.Keyboard;

[Platform("Win")]
[SupportedOSPlatform("windows")]
public class WindowsKeyInjectionTests
{
	private const int WhKeyboardLl = 13;
	private const uint LlkhfExtended = 0x01;
	private const uint LlkhfInjected = 0x10;
	private const uint LlkhfUp = 0x80;
	private const uint PmRemove = 0x0001;
	private const int PumpMs = 250;

	private static readonly bool[] _downThenUp = [false, true];
	private static readonly int[] _lockKeys = [0x90, 0x14, 0x91];

	private static IEnumerable<KeyCode> PressedKeys =>
		Enum.GetValues<KeyCode>()
			.Where(WindowsKeyboardInputProvider.IsExtendedKey)
			.Concat([KeyCode.CapsLock, KeyCode.ScrollLock, KeyCode.Pause]);

	[TestCaseSource(nameof(PressedKeys))]
	public void A_pressed_key_reaches_windows_as_that_key(KeyCode key)
	{
		var events = Press(key);

		var matching = events
			.Where(e => NativeKeys.FromWindows((int)e.VirtualKey, (int)e.ScanCode, e.IsExtended) == key)
			.Select(e => e.IsUp);
		Assert.That(matching, Is.EqualTo(_downThenUp));
	}

	[TestCase(KeyCode.NumLock, 0x90u, 0x45u)]
	[TestCase(KeyCode.PrintScreen, 0x2Cu, 0x37u)]
	public void A_pressed_key_arrives_like_the_physical_key(KeyCode key, uint virtualKey, uint scanCode)
	{
		var events = Press(key);

		Assert.That(events,
			Is.EqualTo(new[]
			{
				new KeyEvent(virtualKey, scanCode, true, false),
				new KeyEvent(virtualKey, scanCode, true, true)
			}));
	}

	private static List<KeyEvent> Press(KeyCode key)
	{
		if (Win32Windows.GetForegroundWindow() == IntPtr.Zero)
		{
			Assert.Ignore("No foreground window in this session (headless or locked desktop).");
		}

		var locks = _lockKeys.ToDictionary(k => k, IsToggled);
		var provider = new WindowsKeyboardInputProvider();
		var events = new List<KeyEvent>();
		_ = WindowsKeyboardInputProvider.TryMapKey(key, GetKeyboardLayout(0), out _, out var sentScanCode);

		HookProc callback = (code, message, data) =>
		{
			var info = Marshal.PtrToStructure<KbdLlHookStruct>(data);
			if (code < 0 || (info.flags & LlkhfInjected) == 0 || info.scanCode != sentScanCode)
			{
				return CallNextHookEx(IntPtr.Zero, code, message, data);
			}

			events.Add(new KeyEvent(info.vkCode,
				info.scanCode,
				(info.flags & LlkhfExtended) != 0,
				(info.flags & LlkhfUp) != 0));
			return 1;
		};

		var hook = SetWindowsHookEx(WhKeyboardLl, callback, GetModuleHandle(null), 0);
		if (hook == IntPtr.Zero)
		{
			Assert.Ignore("A low-level keyboard hook cannot be installed in this session.");
		}

		try
		{
			provider.KeyDown(key);
			Pump();
			provider.KeyUp(key);
			Pump();
		}
		finally
		{
			_ = UnhookWindowsHookEx(hook);
			GC.KeepAlive(callback);
		}

		// A hook that answers too late lets the key through: put the developer's lock keys back.
		RestoreLocks(provider, locks);

		if (events.Count == 0)
		{
			Assert.Ignore("No injected input arrived (no input desktop, or an elevated foreground window).");
		}

		return events;
	}

	private static void RestoreLocks(WindowsKeyboardInputProvider provider, Dictionary<int, bool> locks)
	{
		foreach (var (virtualKey, wasToggled) in locks)
		{
			if (IsToggled(virtualKey) == wasToggled)
			{
				continue;
			}

			var key = NativeKeys.FromWindows(virtualKey, false);
			provider.KeyDown(key);
			provider.KeyUp(key);
			Pump();
		}
	}

	private static bool IsToggled(int virtualKey)
	{
		return (GetKeyState(virtualKey) & 1) != 0;
	}

	private static void Pump()
	{
		var deadline = Environment.TickCount64 + PumpMs;
		while (Environment.TickCount64 < deadline)
		{
			while (PeekMessage(out _, IntPtr.Zero, 0, 0, PmRemove))
			{
			}

			Thread.Sleep(5);
		}
	}

	private sealed record KeyEvent(uint VirtualKey, uint ScanCode, bool IsExtended, bool IsUp);

	private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);

	[StructLayout(LayoutKind.Sequential)]
	private struct KbdLlHookStruct
	{
		public uint vkCode;
		public uint scanCode;
		public uint flags;
		public uint time;
		public IntPtr dwExtraInfo;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct Msg
	{
		public IntPtr hwnd;
		public uint message;
		public IntPtr wParam;
		public IntPtr lParam;
		public uint time;
		public int x;
		public int y;
	}

	[DllImport("user32.dll", SetLastError = true)]
	private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool UnhookWindowsHookEx(IntPtr hhk);

	[DllImport("user32.dll")]
	private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool PeekMessage(
		out Msg lpMsg,
		IntPtr hWnd,
		uint wMsgFilterMin,
		uint wMsgFilterMax,
		uint wRemoveMsg);

	[DllImport("user32.dll")]
	private static extern short GetKeyState(int nVirtKey);

	[DllImport("user32.dll")]
	private static extern IntPtr GetKeyboardLayout(uint idThread);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
	private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
