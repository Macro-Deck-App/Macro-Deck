namespace MacroDeckHost.Platform.Applications;

internal enum ApplicationWindowAvailability
{
	Available,
	Unsupported,
	Wayland,
	AccessibilityNotGranted
}

internal interface IApplicationWindowReader : IDisposable
{
	ApplicationWindowAvailability Availability { get; }

	IReadOnlyDictionary<int, IReadOnlyList<ApplicationWindowState>> Read(IReadOnlySet<int> processIds);
}

internal readonly record struct WindowBounds(double Left, double Top, double Right, double Bottom)
{
	public bool Covers(WindowBounds other)
	{
		return Left <= other.Left && Top <= other.Top && Right >= other.Right && Bottom >= other.Bottom;
	}

	public double Width => Right - Left;

	public double Height => Bottom - Top;
}

internal static class ApplicationWindowStateMapper
{
	// macOS has no maximized flag: a filled window leaves room for the menu bar and the Dock only.
	private const double MacOsDockAllowance = 130;

	private const double MacOsMenuBarAllowance = 45;

	public static ApplicationWindowState FromWindows(
		bool iconic,
		bool zoomed,
		bool hasCaption,
		WindowBounds window,
		WindowBounds monitor)
	{
		if (iconic)
		{
			return ApplicationWindowState.Minimized;
		}

		// A maximized window overhangs every monitor edge by its frame, so with an auto-hiding taskbar
		// it covers the monitor too; only a borderless one is fullscreen then.
		if (window.Covers(monitor) && (!zoomed || !hasCaption))
		{
			return ApplicationWindowState.Fullscreen;
		}

		return zoomed ? ApplicationWindowState.Maximized : ApplicationWindowState.Normal;
	}

	public static ApplicationWindowState FromX11(
		bool hidden,
		bool fullscreen,
		bool maximizedVertically,
		bool maximizedHorizontally)
	{
		if (hidden)
		{
			return ApplicationWindowState.Minimized;
		}

		if (fullscreen)
		{
			return ApplicationWindowState.Fullscreen;
		}

		return maximizedVertically && maximizedHorizontally
			? ApplicationWindowState.Maximized
			: ApplicationWindowState.Normal;
	}

	public static ApplicationWindowState FromMacOs(
		bool minimized,
		bool fullscreen,
		WindowBounds window,
		WindowBounds display)
	{
		if (minimized)
		{
			return ApplicationWindowState.Minimized;
		}

		if (fullscreen || window.Covers(display))
		{
			return ApplicationWindowState.Fullscreen;
		}

		return display.Width - window.Width <= MacOsDockAllowance
			&& display.Height - window.Height <= MacOsDockAllowance + MacOsMenuBarAllowance
				? ApplicationWindowState.Maximized
				: ApplicationWindowState.Normal;
	}
}
