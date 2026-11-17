using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MacroDeckHost.Platform.Applications;

namespace MacroDeckHost.Platform.Native;

[SupportedOSPlatform("macos")]
internal static class MacOsAccessibilityWindows
{
	private const string ApplicationServices = MacOsAccessibility.ApplicationServices;
	private const string CoreFoundation = MacOsAccessibility.CoreFoundation;

	private const int AxErrorSuccess = 0;
	private const int AxValueCgPointType = 1;
	private const int AxValueCgSizeType = 2;
	private const float MessagingTimeoutSeconds = 0.25f;
	private const string StandardWindowSubrole = "AXStandardWindow";

	private static readonly IntPtr _windowsAttribute = MacOsCoreFoundation.CreateCfString("AXWindows");
	private static readonly IntPtr _hiddenAttribute = MacOsCoreFoundation.CreateCfString("AXHidden");
	private static readonly IntPtr _minimizedAttribute = MacOsCoreFoundation.CreateCfString("AXMinimized");
	private static readonly IntPtr _fullScreenAttribute = MacOsCoreFoundation.CreateCfString("AXFullScreen");
	private static readonly IntPtr _subroleAttribute = MacOsCoreFoundation.CreateCfString("AXSubrole");
	private static readonly IntPtr _positionAttribute = MacOsCoreFoundation.CreateCfString("AXPosition");
	private static readonly IntPtr _sizeAttribute = MacOsCoreFoundation.CreateCfString("AXSize");

	public static IReadOnlyList<ApplicationWindowState> ReadWindows(int processId)
	{
		var application = AXUIElementCreateApplication(processId);
		if (application == IntPtr.Zero)
		{
			return [];
		}

		try
		{
			// One unresponsive app must not stall the sampler for the default six seconds per call.
			_ = AXUIElementSetMessagingTimeout(application, MessagingTimeoutSeconds);
			var appHidden = ReadBool(application, _hiddenAttribute);

			if (AXUIElementCopyAttributeValue(application, _windowsAttribute, out var windows) != AxErrorSuccess
				|| windows == IntPtr.Zero)
			{
				return [];
			}

			try
			{
				var states = new List<ApplicationWindowState>();
				var count = CFArrayGetCount(windows);
				for (nint index = 0; index < count; index++)
				{
					var window = CFArrayGetValueAtIndex(windows, index);
					if (window == IntPtr.Zero)
					{
						continue;
					}

					// macOS reports a minimized window, and every window of a hidden app, as AXDialog.
					var minimized = appHidden || ReadBool(window, _minimizedAttribute);
					if (minimized || ReadString(window, _subroleAttribute) == StandardWindowSubrole)
					{
						states.Add(ReadState(window, minimized));
					}
				}

				return states;
			}
			finally
			{
				MacOsCoreFoundation.CFRelease(windows);
			}
		}
		finally
		{
			MacOsCoreFoundation.CFRelease(application);
		}
	}

	private static ApplicationWindowState ReadState(IntPtr window, bool minimized)
	{
		var fullscreen = ReadBool(window, _fullScreenAttribute);
		if (!TryReadBounds(window, out var bounds))
		{
			return minimized
				? ApplicationWindowState.Minimized
				: fullscreen
					? ApplicationWindowState.Fullscreen
					: ApplicationWindowState.Normal;
		}

		// AX and Quartz display bounds share the top-left global origin, so no flip is needed.
		var center = new CgPoint { X = (bounds.Left + bounds.Right) / 2, Y = (bounds.Top + bounds.Bottom) / 2 };
		var displays = new uint[1];
		var display = CGGetDisplaysWithPoint(center, 1, displays, out var found) == 0 && found > 0
			? ToBounds(CGDisplayBounds(displays[0]))
			: bounds;

		return ApplicationWindowStateMapper.FromMacOs(minimized, fullscreen, bounds, display);
	}

	private static bool TryReadBounds(IntPtr window, out WindowBounds bounds)
	{
		bounds = default;
		if (AXUIElementCopyAttributeValue(window, _positionAttribute, out var positionValue) != AxErrorSuccess
			|| positionValue == IntPtr.Zero)
		{
			return false;
		}

		try
		{
			if (AXUIElementCopyAttributeValue(window, _sizeAttribute, out var sizeValue) != AxErrorSuccess
				|| sizeValue == IntPtr.Zero)
			{
				return false;
			}

			try
			{
				if (!AXValueGetValue(positionValue, AxValueCgPointType, out CgPoint position)
					|| !AXValueGetValue(sizeValue, AxValueCgSizeType, out CgSize size))
				{
					return false;
				}

				bounds = new WindowBounds(position.X, position.Y, position.X + size.Width, position.Y + size.Height);
				return true;
			}
			finally
			{
				MacOsCoreFoundation.CFRelease(sizeValue);
			}
		}
		finally
		{
			MacOsCoreFoundation.CFRelease(positionValue);
		}
	}

	private static bool ReadBool(IntPtr element, IntPtr attribute)
	{
		if (AXUIElementCopyAttributeValue(element, attribute, out var value) != AxErrorSuccess || value == IntPtr.Zero)
		{
			return false;
		}

		try
		{
			return CFGetTypeID(value) == CFBooleanGetTypeID() && CFBooleanGetValue(value);
		}
		finally
		{
			MacOsCoreFoundation.CFRelease(value);
		}
	}

	private static string? ReadString(IntPtr element, IntPtr attribute)
	{
		if (AXUIElementCopyAttributeValue(element, attribute, out var value) != AxErrorSuccess || value == IntPtr.Zero)
		{
			return null;
		}

		try
		{
			return MacOsCoreFoundation.IsCfString(value) ? MacOsCoreFoundation.ReadCfString(value) : null;
		}
		finally
		{
			MacOsCoreFoundation.CFRelease(value);
		}
	}

	private static WindowBounds ToBounds(CgRect rect)
	{
		return new WindowBounds(rect.Origin.X,
			rect.Origin.Y,
			rect.Origin.X + rect.Size.Width,
			rect.Origin.Y + rect.Size.Height);
	}

	[DllImport(ApplicationServices)]
	private static extern IntPtr AXUIElementCreateApplication(int processId);

	[DllImport(ApplicationServices)]
	private static extern int AXUIElementSetMessagingTimeout(IntPtr element, float timeoutInSeconds);

	[DllImport(ApplicationServices)]
	private static extern int AXUIElementCopyAttributeValue(IntPtr element, IntPtr attribute, out IntPtr value);

	[DllImport(ApplicationServices)]
	[return: MarshalAs(UnmanagedType.I1)]
	private static extern bool AXValueGetValue(IntPtr value, int type, out CgPoint point);

	[DllImport(ApplicationServices)]
	[return: MarshalAs(UnmanagedType.I1)]
	private static extern bool AXValueGetValue(IntPtr value, int type, out CgSize size);

	[DllImport(ApplicationServices)]
	private static extern int CGGetDisplaysWithPoint(CgPoint point, uint maxDisplays, uint[] displays, out uint count);

	[DllImport(ApplicationServices)]
	private static extern CgRect CGDisplayBounds(uint display);

	[DllImport(CoreFoundation)]
	private static extern nint CFArrayGetCount(IntPtr array);

	[DllImport(CoreFoundation)]
	private static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, nint index);

	[DllImport(CoreFoundation)]
	private static extern nuint CFGetTypeID(IntPtr value);

	[DllImport(CoreFoundation)]
	private static extern nuint CFBooleanGetTypeID();

	[DllImport(CoreFoundation)]
	[return: MarshalAs(UnmanagedType.I1)]
	private static extern bool CFBooleanGetValue(IntPtr value);

	[StructLayout(LayoutKind.Sequential)]
	private struct CgPoint
	{
		public double X;
		public double Y;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct CgSize
	{
		public double Width;
		public double Height;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct CgRect
	{
		public CgPoint Origin;
		public CgSize Size;
	}
}
