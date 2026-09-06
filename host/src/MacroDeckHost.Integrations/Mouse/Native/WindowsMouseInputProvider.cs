using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MacroDeck.Localization;
using MacroDeckHost.Integrations.Native;
using Input = MacroDeckHost.Integrations.Native.Win32Input.Input;
using InputUnion = MacroDeckHost.Integrations.Native.Win32Input.InputUnion;
using Mouseinput = MacroDeckHost.Integrations.Native.Win32Input.Mouseinput;

namespace MacroDeckHost.Integrations.Mouse.Native;

[SupportedOSPlatform("windows")]
public sealed class WindowsMouseInputProvider : IMouseInputProvider
{
	private const uint InputMouse = Win32Input.InputMouse;

	private const uint MouseEventFMove = 0x0001;
	private const uint MouseEventFLeftDown = 0x0002;
	private const uint MouseEventFLeftUp = 0x0004;
	private const uint MouseEventFRightDown = 0x0008;
	private const uint MouseEventFRightUp = 0x0010;
	private const uint MouseEventFMiddleDown = 0x0020;
	private const uint MouseEventFMiddleUp = 0x0040;
	private const uint MouseEventFXDown = 0x0080;
	private const uint MouseEventFXUp = 0x0100;
	private const uint MouseEventFWheel = 0x0800;
	private const uint MouseEventFHWheel = 0x1000;
	private const uint MouseEventFMoveNoCoalesce = 0x2000;
	private const uint MouseEventFVirtualDesk = 0x4000;
	private const uint MouseEventFAbsolute = 0x8000;

	private const uint XButton1 = 0x0001;
	private const uint XButton2 = 0x0002;

	private const int WheelDelta = 120;

	private const int SmXVirtualScreen = 76;
	private const int SmYVirtualScreen = 77;
	private const int SmCxVirtualScreen = 78;
	private const int SmCyVirtualScreen = 79;

	public string PlatformName => "Windows (SendInput)";

	public bool IsSupported => OperatingSystem.IsWindows();

	public bool IsDegraded => false;

	public LocalizedText DegradedReason => default;

	public bool RequiresPermission => false;

	public bool HasPermission => true;

	public void RequestPermission()
	{
	}

	public bool TryGetPosition(out MousePoint position)
	{
		if (GetCursorPos(out var point))
		{
			position = new MousePoint(point.x, point.y);
			return true;
		}

		position = default;
		return false;
	}

	public bool TryGetDesktopBounds(out MouseRect bounds)
	{
		var left = GetSystemMetrics(SmXVirtualScreen);
		var top = GetSystemMetrics(SmYVirtualScreen);
		var width = GetSystemMetrics(SmCxVirtualScreen);
		var height = GetSystemMetrics(SmCyVirtualScreen);
		if (width <= 0 || height <= 0)
		{
			bounds = default;
			return false;
		}

		bounds = new MouseRect(left, top, width, height);
		return true;
	}

	public void MoveTo(MousePoint position)
	{
		if (TryMoveEvent(position, 0, out var move))
		{
			Send([move]);
		}
	}

	public void ButtonDown(MouseButton button, MousePoint? position)
	{
		var inputs = new List<Input>(2);
		if (position is { } point && TryMoveEvent(point, 0, out var move))
		{
			inputs.Add(move);
		}

		inputs.Add(MouseEvent(DownFlag(button), mouseData: XData(button)));
		Send(inputs);
	}

	public void ButtonUp(MouseButton button)
		=> Send([MouseEvent(UpFlag(button), mouseData: XData(button))]);

	public void Click(MouseButton button, int clickCount, MousePoint? position)
	{
		var clicks = Math.Clamp(clickCount, 1, 3);
		var inputs = new List<Input>(clicks * 2 + 1);
		if (position is { } point && TryMoveEvent(point, 0, out var move))
		{
			inputs.Add(move);
		}

		var down = MouseEvent(DownFlag(button), mouseData: XData(button));
		var up = MouseEvent(UpFlag(button), mouseData: XData(button));
		for (var click = 0; click < clicks; click++)
		{
			inputs.Add(down);
			inputs.Add(up);
		}

		Send(inputs);
	}

	public void DragTo(MouseButton button, MousePoint position)
	{
		if (TryMoveEvent(position, MouseEventFMoveNoCoalesce, out var move))
		{
			Send([move]);
		}
	}

	public void Scroll(ScrollAxis axis, int notches)
	{
		var flags = axis == ScrollAxis.Horizontal ? MouseEventFHWheel : MouseEventFWheel;

		Send([MouseEvent(flags, mouseData: unchecked((uint)(notches * WheelDelta)))]);
	}

	private static bool TryMoveEvent(MousePoint position, uint extraFlags, out Input input)
	{
		var left = GetSystemMetrics(SmXVirtualScreen);
		var top = GetSystemMetrics(SmYVirtualScreen);
		var width = GetSystemMetrics(SmCxVirtualScreen);
		var height = GetSystemMetrics(SmCyVirtualScreen);
		if (width <= 0 || height <= 0)
		{
			input = default;
			return false;
		}

		input = MouseEvent(MouseEventFMove | MouseEventFAbsolute | MouseEventFVirtualDesk | extraFlags,
			Win32AbsoluteCoordinates.Normalize(position.X - left, width),
			Win32AbsoluteCoordinates.Normalize(position.Y - top, height));
		return true;
	}

	private static Input MouseEvent(uint flags, int dx = 0, int dy = 0, uint mouseData = 0)
		=> new()
		{
			type = InputMouse,
			union = new InputUnion
			{
				mouse = new Mouseinput
				{
					dx = dx,
					dy = dy,
					mouseData = mouseData,
					dwFlags = flags,
					time = 0,
					dwExtraInfo = IntPtr.Zero
				}
			}
		};

	private static void Send(IReadOnlyCollection<Input> inputs) => Win32Input.Send(inputs);

	private static uint DownFlag(MouseButton button) => button switch
	{
		MouseButton.Right => MouseEventFRightDown,
		MouseButton.Middle => MouseEventFMiddleDown,
		MouseButton.Back or MouseButton.Forward => MouseEventFXDown,
		_ => MouseEventFLeftDown
	};

	private static uint UpFlag(MouseButton button) => button switch
	{
		MouseButton.Right => MouseEventFRightUp,
		MouseButton.Middle => MouseEventFMiddleUp,
		MouseButton.Back or MouseButton.Forward => MouseEventFXUp,
		_ => MouseEventFLeftUp
	};

	private static uint XData(MouseButton button) => button switch
	{
		MouseButton.Back => XButton1,
		MouseButton.Forward => XButton2,
		_ => 0
	};

	[DllImport("user32.dll")]
	private static extern int GetSystemMetrics(int nIndex);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetCursorPos(out NativePoint point);

	[StructLayout(LayoutKind.Sequential)]
	private struct NativePoint
	{
		public int x;
		public int y;
	}
}
