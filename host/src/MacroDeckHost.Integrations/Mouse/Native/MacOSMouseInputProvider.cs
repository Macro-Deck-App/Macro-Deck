using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MacroDeck.Localization;
using MacroDeckHost.Integrations.Native;

namespace MacroDeckHost.Integrations.Mouse.Native;

[SupportedOSPlatform("macos")]
public sealed class MacOsMouseInputProvider : IMouseInputProvider
{
	private const string ApplicationServices = MacOsAccessibility.ApplicationServices;

	private const string CoreFoundation = MacOsAccessibility.CoreFoundation;

	private const uint HidEventTap = 0; // kCGHIDEventTap

	private const uint EventLeftMouseDown = 1; // kCGEventLeftMouseDown
	private const uint EventLeftMouseUp = 2; // kCGEventLeftMouseUp
	private const uint EventRightMouseDown = 3; // kCGEventRightMouseDown
	private const uint EventRightMouseUp = 4; // kCGEventRightMouseUp
	private const uint EventMouseMoved = 5; // kCGEventMouseMoved
	private const uint EventLeftMouseDragged = 6; // kCGEventLeftMouseDragged
	private const uint EventRightMouseDragged = 7; // kCGEventRightMouseDragged
	private const uint EventOtherMouseDown = 25; // kCGEventOtherMouseDown
	private const uint EventOtherMouseUp = 26; // kCGEventOtherMouseUp
	private const uint EventOtherMouseDragged = 27; // kCGEventOtherMouseDragged

	private const uint MouseButtonLeft = 0; // kCGMouseButtonLeft
	private const uint MouseButtonRight = 1; // kCGMouseButtonRight
	private const uint MouseButtonCenter = 2; // kCGMouseButtonCenter

	private const uint MouseButtonBack = 3;
	private const uint MouseButtonForward = 4;

	private const uint MouseEventClickState = 1; // kCGMouseEventClickState
	private const uint MouseEventButtonNumber = 3; // kCGMouseEventButtonNumber
	private const uint MouseEventDeltaX = 4; // kCGMouseEventDeltaX
	private const uint MouseEventDeltaY = 5; // kCGMouseEventDeltaY

	private const uint ScrollEventUnitLine = 1; // kCGScrollEventUnitLine
	private const uint ScrollWheelCount = 2;

	private const int ErrorSuccess = 0; // kCGErrorSuccess

	private const double RetinaScale = 2.0;

	private readonly List<MouseButton> _heldButtons = [];

	private CGPoint _lastLocation;

	private bool _hasLastLocation;

	public string PlatformName => "macOS (Quartz CGEvent)";

	public bool IsSupported => OperatingSystem.IsMacOS();

	public bool IsDegraded => false;

	public LocalizedText DegradedReason => default;

	public bool RequiresPermission => true;

	public bool HasPermission => MacOsAccessibility.IsTrusted();

	public void RequestPermission() => MacOsAccessibility.RequestTrust();

	public bool TryGetPosition(out MousePoint position)
	{
		if (TryGetCursorLocation(out var location))
		{
			position = ToDesktopPixels(location, GetDesktopScale());
			return true;
		}

		position = default;
		return false;
	}

	public bool TryGetDesktopBounds(out MouseRect bounds)
	{
		bounds = default;

		var displays = GetActiveDisplays();
		if (displays.Length == 0)
		{
			return false;
		}

		var left = double.MaxValue;
		var top = double.MaxValue;
		var right = double.MinValue;
		var bottom = double.MinValue;
		foreach (var display in displays)
		{
			var frame = CGDisplayBounds(display);
			left = Math.Min(left, frame.origin.x);
			top = Math.Min(top, frame.origin.y);
			right = Math.Max(right, frame.origin.x + frame.size.width);
			bottom = Math.Max(bottom, frame.origin.y + frame.size.height);
		}

		if (right <= left || bottom <= top)
		{
			return false;
		}

		var scale = GetDesktopScale();
		bounds = new MouseRect((int)Math.Round(left * scale),
			(int)Math.Round(top * scale),
			(int)Math.Round((right - left) * scale),
			(int)Math.Round((bottom - top) * scale));
		return true;
	}

	public void MoveTo(MousePoint position) => PostMove(ToGlobalPoint(position, GetDesktopScale()));

	public void ButtonDown(MouseButton button, MousePoint? position)
	{
		if (!TryResolveLocation(position, out var location))
		{
			location = _lastLocation;
		}

		if (position is not null)
		{
			PostMove(location);
		}

		PostButton(DownType(button), button, location, clickState: 1);
		HoldButton(button);
	}

	public void ButtonUp(MouseButton button)
	{
		_heldButtons.Remove(button);

		var location = TryGetCursorLocation(out var current) ? current : _lastLocation;
		PostButton(UpType(button), button, location, clickState: 1);

		if (_heldButtons.Count == 0)
		{
			_hasLastLocation = false;
		}
	}

	public void Click(MouseButton button, int clickCount, MousePoint? position)
	{
		if (!TryResolveLocation(position, out var location))
		{
			return;
		}

		if (position is not null)
		{
			PostMove(location);
		}

		var clicks = Math.Clamp(clickCount, 1, 3);
		for (var click = 1; click <= clicks; click++)
		{
			PostButton(DownType(button), button, location, click);
			PostButton(UpType(button), button, location, click);
		}
	}

	public void DragTo(MouseButton button, MousePoint position)
		=> PostMotion(DraggedType(button), ToGlobalPoint(position, GetDesktopScale()), ButtonIndex(button));

	public void Scroll(ScrollAxis axis, int notches)
	{
		var vertical = axis == ScrollAxis.Vertical ? notches : 0;
		var horizontal = axis == ScrollAxis.Horizontal ? notches : 0;

		var handle = CGEventCreateScrollWheelEvent2(IntPtr.Zero,
			ScrollEventUnitLine,
			ScrollWheelCount,
			vertical,
			horizontal,
			0);
		if (handle == IntPtr.Zero)
		{
			return;
		}

		try
		{
			CGEventPost(HidEventTap, handle);
		}
		finally
		{
			CFRelease(handle);
		}
	}

	private static CGPoint ToGlobalPoint(MousePoint position, double scale)
		=> new() { x = position.X / scale, y = position.Y / scale };

	private static MousePoint ToDesktopPixels(CGPoint location, double scale)
		=> new((int)Math.Round(location.x * scale), (int)Math.Round(location.y * scale));

	private static double GetDesktopScale()
	{
		var mode = CGDisplayCopyDisplayMode(CGMainDisplayID());
		if (mode == IntPtr.Zero)
		{
			return 1.0;
		}

		try
		{
			var pointWidth = CGDisplayModeGetWidth(mode);
			var pixelWidth = CGDisplayModeGetPixelWidth(mode);
			return pointWidth > 0 && pixelWidth > pointWidth ? RetinaScale : 1.0;
		}
		finally
		{
			CGDisplayModeRelease(mode);
		}
	}

	private static uint[] GetActiveDisplays()
	{
		if (CGGetActiveDisplayList(0, null, out var count) != ErrorSuccess || count == 0)
		{
			return [];
		}

		var displays = new uint[count];
		return CGGetActiveDisplayList(count, displays, out _) == ErrorSuccess ? displays : [];
	}

	private static bool TryResolveLocation(MousePoint? position, out CGPoint location)
	{
		if (position is { } point)
		{
			location = ToGlobalPoint(point, GetDesktopScale());
			return true;
		}

		return TryGetCursorLocation(out location);
	}

	private static bool TryGetCursorLocation(out CGPoint location)
	{
		var handle = CGEventCreate(IntPtr.Zero);
		if (handle == IntPtr.Zero)
		{
			location = default;
			return false;
		}

		try
		{
			location = CGEventGetLocation(handle);
			return true;
		}
		finally
		{
			CFRelease(handle);
		}
	}

	private void PostMove(CGPoint location)
	{
		if (LastHeldButton() is { } button)
		{
			PostMotion(DraggedType(button), location, ButtonIndex(button));
			return;
		}

		PostMotion(EventMouseMoved, location, MouseButtonLeft);
	}

	private void PostMotion(uint eventType, CGPoint location, uint button)
	{
		var (deltaX, deltaY) = DeltaTo(location);

		var handle = CGEventCreateMouseEvent(IntPtr.Zero, eventType, location, button);
		if (handle == IntPtr.Zero)
		{
			return;
		}

		try
		{
			CGEventSetIntegerValueField(handle, MouseEventButtonNumber, button);
			CGEventSetIntegerValueField(handle, MouseEventDeltaX, deltaX);
			CGEventSetIntegerValueField(handle, MouseEventDeltaY, deltaY);
			CGEventPost(HidEventTap, handle);
			RememberLocation(location);
		}
		finally
		{
			CFRelease(handle);
		}
	}

	private void PostButton(uint eventType, MouseButton button, CGPoint location, int clickState)
	{
		var index = ButtonIndex(button);
		var handle = CGEventCreateMouseEvent(IntPtr.Zero, eventType, location, index);
		if (handle == IntPtr.Zero)
		{
			return;
		}

		try
		{
			// The creation argument is ignored for the left and right types, but kCGEventOtherMouse*
			// needs the index in both places or the target cannot tell middle from back from forward.
			CGEventSetIntegerValueField(handle, MouseEventButtonNumber, index);
			CGEventSetIntegerValueField(handle, MouseEventClickState, clickState);
			CGEventPost(HidEventTap, handle);

			RememberLocation(location);
		}
		finally
		{
			CFRelease(handle);
		}
	}

	private (long X, long Y) DeltaTo(CGPoint location)
	{
		if (!_hasLastLocation)
		{
			if (!TryGetCursorLocation(out var current))
			{
				return (0, 0);
			}

			RememberLocation(current);
		}

		return ((long)Math.Round(location.x - _lastLocation.x), (long)Math.Round(location.y - _lastLocation.y));
	}

	private void RememberLocation(CGPoint location)
	{
		_lastLocation = location;
		_hasLastLocation = true;
	}

	private void HoldButton(MouseButton button)
	{
		_heldButtons.Remove(button);
		_heldButtons.Add(button);
	}

	private MouseButton? LastHeldButton() => _heldButtons.Count > 0 ? _heldButtons[^1] : null;

	private static uint ButtonIndex(MouseButton button) => button switch
	{
		MouseButton.Right => MouseButtonRight,
		MouseButton.Middle => MouseButtonCenter,
		MouseButton.Back => MouseButtonBack,
		MouseButton.Forward => MouseButtonForward,
		_ => MouseButtonLeft
	};

	private static uint DownType(MouseButton button) => button switch
	{
		MouseButton.Left => EventLeftMouseDown,
		MouseButton.Right => EventRightMouseDown,
		_ => EventOtherMouseDown
	};

	private static uint UpType(MouseButton button) => button switch
	{
		MouseButton.Left => EventLeftMouseUp,
		MouseButton.Right => EventRightMouseUp,
		_ => EventOtherMouseUp
	};

	private static uint DraggedType(MouseButton button) => button switch
	{
		MouseButton.Left => EventLeftMouseDragged,
		MouseButton.Right => EventRightMouseDragged,
		_ => EventOtherMouseDragged
	};

	[DllImport(ApplicationServices)]
	private static extern IntPtr CGEventCreate(IntPtr source);

	[DllImport(ApplicationServices)]
	private static extern IntPtr CGEventCreateMouseEvent(
		IntPtr source,
		uint mouseType,
		CGPoint mouseCursorPosition,
		uint mouseButton);

	[DllImport(ApplicationServices)]
	private static extern IntPtr CGEventCreateScrollWheelEvent2(
		IntPtr source,
		uint units,
		uint wheelCount,
		int wheel1,
		int wheel2,
		int wheel3);

	[DllImport(ApplicationServices)]
	private static extern CGPoint CGEventGetLocation(IntPtr handle);

	[DllImport(ApplicationServices)]
	private static extern void CGEventSetIntegerValueField(IntPtr handle, uint field, long value);

	[DllImport(ApplicationServices)]
	private static extern void CGEventPost(uint tap, IntPtr handle);

	[DllImport(ApplicationServices)]
	private static extern uint CGMainDisplayID();

	[DllImport(ApplicationServices)]
	private static extern CGRect CGDisplayBounds(uint display);

	[DllImport(ApplicationServices)]
	private static extern int CGGetActiveDisplayList(
		uint maxDisplays,
		[Out] uint[]? activeDisplays,
		out uint displayCount);

	[DllImport(ApplicationServices)]
	private static extern IntPtr CGDisplayCopyDisplayMode(uint display);

	[DllImport(ApplicationServices)]
	private static extern nuint CGDisplayModeGetWidth(IntPtr mode);

	[DllImport(ApplicationServices)]
	private static extern nuint CGDisplayModeGetPixelWidth(IntPtr mode);

	[DllImport(ApplicationServices)]
	private static extern void CGDisplayModeRelease(IntPtr mode);

	[DllImport(CoreFoundation)]
	private static extern void CFRelease(IntPtr handle);

	[StructLayout(LayoutKind.Sequential)]
	private struct CGPoint
	{
		public double x;
		public double y;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct CGSize
	{
		public double width;
		public double height;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct CGRect
	{
		public CGPoint origin;
		public CGSize size;
	}
}
