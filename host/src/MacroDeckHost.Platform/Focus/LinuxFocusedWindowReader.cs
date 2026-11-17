using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MacroDeckHost.Platform.Native;

namespace MacroDeckHost.Platform.Focus;

[SupportedOSPlatform("linux")]
public sealed class LinuxFocusedWindowReader : IFocusedWindowReader, IDisposable
{
	private const string LibX11 = "libX11.so.6";

	private readonly X11Connection _connection;
	private nuint _root;

	public LinuxFocusedWindowReader()
		: this(null)
	{
	}

	internal LinuxFocusedWindowReader(string? displayName)
	{
		_connection = new X11Connection(Attach,
			() => new PlatformNotSupportedException("The X11 display is not available."),
			displayName);
	}

	public bool IsSupported => _connection.IsAvailable;

	public string? UnsupportedReason => IsSupported
		? null
		: "No X11 display available (Wayland without XWayland is not supported).";

	public bool IsDegraded => IsSupported && LinuxDisplaySession.IsWayland;

	public FocusedAppInfo? Read()
	{
		try
		{
			if (_connection.RunIfConnected(ReadActivePid, null) is not { } pid)
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
			return File.ResolveLinkTarget($"/proc/{pid}/exe", true)?.FullName;
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

	private bool Attach(IntPtr display)
	{
		_root = XDefaultRootWindow(display);
		return true;
	}

	private int? ReadActivePid(IntPtr display)
	{
		var window = X11Windows.GetActiveWindow(display, _root);
		return window == 0 ? null : X11Windows.GetWindowPid(display, window);
	}

	public void Dispose()
	{
		_connection.Dispose();
	}

	[DllImport(LibX11)]
	private static extern nuint XDefaultRootWindow(IntPtr display);
}
