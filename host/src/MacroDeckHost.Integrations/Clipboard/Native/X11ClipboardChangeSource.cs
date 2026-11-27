using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using MacroDeckHost.Platform.Native;

namespace MacroDeckHost.Integrations.Clipboard.Native;

[SupportedOSPlatform("linux")]
internal sealed class X11ClipboardChangeSource : IClipboardChangeSource
{
	private const int PollTimeoutMilliseconds = 500;
	private const short PollIn = 0x001;
	private const short PollErr = 0x008;
	private const short PollHup = 0x010;
	private const short PollNval = 0x020;
	private const nuint SetSelectionOwnerNotifyMask = 1;
	private const int SelectionNotify = 0;

	// An XEvent is a union padded to 24 longs.
	private static readonly int _eventSize = 24 * IntPtr.Size;

	private static readonly TimeSpan _joinTimeout = TimeSpan.FromSeconds(2);

	private readonly string? _displayName;
	private readonly ManualResetEventSlim _stop = new();
	private readonly Lock _gate = new();

	private Thread? _thread;
	private int _connectionFd = -1;
	private int _eventBase;
	private nuint _clipboardAtom;
	private bool _extensionMissing;
	private bool _unsupported;

	public X11ClipboardChangeSource(string? displayName = null)
	{
		_displayName = displayName;
	}

	public event Action<long?>? Changed;

	public event Action? WatchUnsupported;

	public bool IsWatchUnsupported => Volatile.Read(ref _unsupported);

	public void Start()
	{
		lock (_gate)
		{
			if (_stop.IsSet || _thread is not null)
			{
				return;
			}

			if (!X11Clipboard.IsAvailable)
			{
				ReportUnsupported();
				return;
			}

			_thread = new Thread(Run) { IsBackground = true, Name = "Macro Deck X11 clipboard watch" };
			_thread.Start();
		}
	}

	public void Dispose()
	{
		Thread? thread;
		lock (_gate)
		{
			if (_stop.IsSet)
			{
				return;
			}

			_stop.Set();
			thread = _thread;
		}

		if (thread is not null && thread != Thread.CurrentThread)
		{
			_ = thread.Join(_joinTimeout);
		}
	}

	private void Run()
	{
		var buffer = Marshal.AllocHGlobal(_eventSize);
		var connection = new X11Connection(Attach,
			() => new ClipboardException(ClipboardFailure.Unavailable),
			_displayName);
		try
		{
			while (!_stop.IsSet)
			{
				if (Volatile.Read(ref _extensionMissing))
				{
					ReportUnsupported();
					return;
				}

				if (!connection.IsAvailable)
				{
					_ = _stop.Wait(PollTimeoutMilliseconds);
					continue;
				}

				var generation = connection.Generation;
				var revents = X11Clipboard.Poll(Volatile.Read(ref _connectionFd), PollIn, PollTimeoutMilliseconds);
				if (_stop.IsSet)
				{
					return;
				}

				var changes = 0;
				var connected = connection.TryRun(display => changes = Drain(display, buffer), generation);
				for (var index = 0; index < changes && !_stop.IsSet; index++)
				{
					Changed?.Invoke(null);
				}

				if (!connected || (revents & (PollErr | PollHup | PollNval)) != 0 && changes == 0)
				{
					_ = _stop.Wait(PollTimeoutMilliseconds);
				}
			}
		}
		finally
		{
			connection.Dispose();
			Marshal.FreeHGlobal(buffer);
		}
	}

	private bool Attach(IntPtr display)
	{
		if (!X11Clipboard.XFixesQueryExtension(display, out var eventBase, out _))
		{
			Volatile.Write(ref _extensionMissing, true);
			return false;
		}

		var atom = X11Clipboard.XInternAtom(display, Encoding.ASCII.GetBytes("CLIPBOARD\0"), false);
		if (atom == 0)
		{
			return false;
		}

		X11Clipboard.XFixesSelectSelectionInput(display,
			X11Windows.DefaultRootWindow(display),
			atom,
			SetSelectionOwnerNotifyMask);
		X11Windows.Flush(display);
		_eventBase = eventBase;
		_clipboardAtom = atom;
		Volatile.Write(ref _connectionFd, X11Clipboard.XConnectionNumber(display));
		return true;
	}

	private int Drain(IntPtr display, IntPtr buffer)
	{
		var changes = 0;
		while (X11Clipboard.XPending(display) > 0)
		{
			X11Windows.ThrowIfLost(display);
			_ = X11Clipboard.XNextEvent(display, buffer);
			var notify = Marshal.PtrToStructure<X11Clipboard.SelectionNotifyEvent>(buffer);
			if (notify.Type == _eventBase + SelectionNotify && notify.Selection == _clipboardAtom)
			{
				changes++;
			}
		}

		return changes;
	}

	private void ReportUnsupported()
	{
		if (Volatile.Read(ref _unsupported))
		{
			return;
		}

		Volatile.Write(ref _unsupported, true);
		WatchUnsupported?.Invoke();
	}
}

[SupportedOSPlatform("linux")]
internal static class X11Clipboard
{
	private const string LibX11 = "libX11.so.6";
	private const string LibXfixes = "libXfixes.so.3";
	private const string LibC = "libc";

	private static readonly Lazy<bool> _available
		= new(() => NativeLibrary.TryLoad(LibX11, out _) && NativeLibrary.TryLoad(LibXfixes, out _));

	public static bool IsAvailable => _available.Value;

	public static short Poll(int fd, short events, int timeoutMilliseconds)
	{
		if (fd < 0)
		{
			Thread.Sleep(timeoutMilliseconds);
			return 0;
		}

		var descriptor = new PollFd { Fd = fd, Events = events };
		return poll(ref descriptor, 1, timeoutMilliseconds) > 0 ? descriptor.Revents : (short)0;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct SelectionNotifyEvent
	{
		public int Type;
		public nuint Serial;
		public int SendEvent;
		public IntPtr Display;
		public nuint Window;
		public int Subtype;
		public nuint Owner;
		public nuint Selection;
		public nuint Timestamp;
		public nuint SelectionTimestamp;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct PollFd
	{
		public int Fd;
		public short Events;
		public short Revents;
	}

	[DllImport(LibXfixes)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool XFixesQueryExtension(IntPtr display, out int eventBase, out int errorBase);

	[DllImport(LibXfixes)]
	public static extern void XFixesSelectSelectionInput(IntPtr display, nuint window, nuint selection, nuint mask);

	[DllImport(LibX11)]
	public static extern nuint XInternAtom(
		IntPtr display,
		byte[] name,
		[MarshalAs(UnmanagedType.Bool)] bool onlyIfExists);

	[DllImport(LibX11)]
	public static extern int XConnectionNumber(IntPtr display);

	[DllImport(LibX11)]
	public static extern int XPending(IntPtr display);

	[DllImport(LibX11)]
	public static extern int XNextEvent(IntPtr display, IntPtr eventReturn);

	[DllImport(LibC, SetLastError = true)]
	private static extern int poll(ref PollFd descriptors, nuint count, int timeout);
}
