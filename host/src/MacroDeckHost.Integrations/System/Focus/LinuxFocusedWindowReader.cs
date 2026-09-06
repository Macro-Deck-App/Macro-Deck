using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MacroDeckHost.Integrations.Native;

namespace MacroDeckHost.Integrations.System.Focus;

[SupportedOSPlatform("linux")]
public sealed class LinuxFocusedWindowReader : IFocusedWindowReader, IDisposable
{
	private const string LibX11 = "libX11.so.6";

	private readonly IntPtr _display;
	private readonly nuint _root;
	private bool _disposed;

	public LinuxFocusedWindowReader()
	{
		_display = OperatingSystem.IsLinux() ? SafeOpenDisplay() : IntPtr.Zero;
		if (_display != IntPtr.Zero)
		{
			_root = XDefaultRootWindow(_display);
		}
	}

	public bool IsSupported => _display != IntPtr.Zero;

	public string? UnsupportedReason => IsSupported
		? null
		: "No X11 display available (Wayland without XWayland is not supported).";

	public FocusedAppInfo? Read()
	{
		if (_display == IntPtr.Zero)
		{
			return null;
		}

		try
		{
			var window = X11Windows.GetActiveWindow(_display, _root);
			if (window == 0)
			{
				return null;
			}

			if (X11Windows.GetWindowPid(_display, window) is not { } pid)
			{
				return null;
			}

			var path = TryReadExePath(pid);
			var name = TryReadCommName(pid);
			return new FocusedAppInfo(pid, path, name, null);
		}
		catch
		{
			return null;
		}
	}

	// /proc/<pid>/exe requires the same uid (or CAP_SYS_PTRACE); a foreign-owned process yields null.
	private static string? TryReadExePath(int pid)
	{
		try
		{
			return File.ResolveLinkTarget($"/proc/{pid}/exe", returnFinalTarget: true)?.FullName;
		}
		catch (IOException)
		{
			return null;
		}
		catch (UnauthorizedAccessException)
		{
			return null;
		}
	}

	private static string? TryReadCommName(int pid)
	{
		try
		{
			var comm = File.ReadAllText($"/proc/{pid}/comm").Trim();
			return comm.Length > 0 ? comm : null;
		}
		catch (IOException)
		{
			return null;
		}
		catch (UnauthorizedAccessException)
		{
			return null;
		}
	}

	private static IntPtr SafeOpenDisplay()
	{
		try
		{
			return XOpenDisplay(IntPtr.Zero);
		}
		catch (DllNotFoundException)
		{
			return IntPtr.Zero;
		}
	}

	public void Dispose()
	{
		if (_disposed || _display == IntPtr.Zero)
		{
			return;
		}

		_disposed = true;
		_ = XCloseDisplay(_display);
	}

	[DllImport(LibX11)]
	private static extern IntPtr XOpenDisplay(IntPtr display);

	[DllImport(LibX11)]
	private static extern int XCloseDisplay(IntPtr display);

	[DllImport(LibX11)]
	private static extern nuint XDefaultRootWindow(IntPtr display);
}
