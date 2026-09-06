using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Threading.Channels;
using MacroDeckHost.Integrations.Native;
using Serilog;

namespace MacroDeckHost.Integrations.System.Focus;

[SupportedOSPlatform("windows")]
internal sealed class WindowsFocusedApplicationWatcher : IFocusedApplicationWatcher
{
	private const int ChannelCapacity = 16;
	private static readonly TimeSpan _hookInstallTimeout = TimeSpan.FromSeconds(5);
	private static readonly TimeSpan _pumpJoinTimeout = TimeSpan.FromSeconds(2);

	private static readonly ILogger _logger = Log.ForContext<WindowsFocusedApplicationWatcher>();

	private readonly Channel<FocusedAppInfo> _channel = Channel.CreateBounded<FocusedAppInfo>(
		new BoundedChannelOptions(ChannelCapacity)
		{
			FullMode = BoundedChannelFullMode.DropOldest,
			SingleReader = true
		});

	// A plain object, not System.Threading.Lock: this namespace's own sibling
	// MacroDeckHost.Integrations.System.Lock shadows the BCL type name here.
	private readonly object _gate = new();

	// SetWinEventHook does not keep this delegate alive on its own; it must stay reachable as a
	// readonly instance field for as long as the hook is installed, or the GC could collect it out
	// from under a callback the OS may still invoke.
	private readonly Win32WinEvent.WinEventProc _proc;

	private readonly Thread _pumpThread;

	private volatile bool _hookInstalled;
	private volatile bool _stopRequested;
	private volatile uint _pumpThreadId;
	private int _disposed;

	public WindowsFocusedApplicationWatcher()
	{
		_proc = OnForegroundChanged;

		// Deliberately not disposed: on the timeout path below this constructor returns while the pump
		// thread may still be about to signal, and Set() on a disposed event throws - on a background
		// thread, which would take the process down. The handle is reclaimed by its own finalizer.
		var ready = new ManualResetEventSlim(false);
		_pumpThread = new Thread(() => RunMessagePump(ready))
		{
			IsBackground = true,
			Name = "MacroDeck Windows focus watcher"
		};
		_pumpThread.Start();

		if (!ready.Wait(_hookInstallTimeout))
		{
			// The pump thread hasn't reached the ready signal yet (or never will). Ask it to stop as
			// soon as it gets there instead of leaving it to arm the hook and pump forever with nothing
			// able to stop it: IsSupported below will report false, so the factory disposes this
			// instance without ever knowing a hook install was in flight.
			_stopRequested = true;
		}
	}

	// Exposed for tests: Dispose is meant to leave no pump thread running, and asserting IsAlive is the
	// only way to observe that rather than a join timeout that passes whether or not the thread died.
	internal bool PumpThreadIsAlive => _pumpThread.IsAlive;

	public bool IsSupported => _hookInstalled;

	// Never a new string: the UI renders this verbatim and every existing value already ships
	// translations. Windows is always a supported platform for focus detection (WindowsFocusedWindowReader
	// always returns null here) - a failed hook installation falls back to polling instead.
	public string? UnsupportedReason => null;

	public async IAsyncEnumerable<FocusedAppInfo> WatchAsync(
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		// The hook is installed in the constructor, so a callback may already be queueing an event
		// before this enumeration ever starts. The seed read and every callback write share one lock,
		// with the seed's read inside the lock, so "read seed -> callback writes a newer focus -> write
		// seed" can never clobber the newer value with a stale one.
		lock (_gate)
		{
			if (ReadForegroundWindow() is { } seed)
			{
				_channel.Writer.TryWrite(seed);
			}
		}

		await foreach (var info in _channel.Reader.ReadAllAsync(cancellationToken))
		{
			yield return info;
		}
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		// Set before WM_QUIT is posted: a hook install still racing the constructor's wait timeout must
		// also converge on stopping, the same way the timeout path does, rather than arming the hook and
		// pumping forever with nothing left able to stop it.
		_stopRequested = true;

		if (_hookInstalled)
		{
			if (!Win32WinEvent.PostThreadMessage(_pumpThreadId, Win32WinEvent.WM_QUIT, IntPtr.Zero, IntPtr.Zero))
			{
				_logger.Warning(
					"Failed to post WM_QUIT to the Windows focus-watcher pump thread; it may not terminate");
			}

			_pumpThread.Join(_pumpJoinTimeout);
		}

		_channel.Writer.TryComplete();
	}

	private void RunMessagePump(ManualResetEventSlim ready)
	{
		_pumpThreadId = Win32WinEvent.GetCurrentThreadId();

		// A thread only gets a message queue once it first calls a message function, and
		// PostThreadMessage fails with ERROR_INVALID_THREAD_ID against a thread with none yet. Force
		// queue creation before signalling ready, so Dispose's later PostThreadMessage(WM_QUIT) is
		// guaranteed deliverable.
		Win32WinEvent.PeekMessage(out _,
			IntPtr.Zero,
			Win32WinEvent.WM_USER,
			Win32WinEvent.WM_USER,
			Win32WinEvent.PM_NOREMOVE);

		var hook = IntPtr.Zero;
		try
		{
			// WINEVENT_OUTOFCONTEXT callbacks are delivered to the installing thread's message queue,
			// so the hook must be installed on the same thread that runs the pump loop below.
			hook = Win32WinEvent.SetWinEventHook(Win32WinEvent.EVENT_SYSTEM_FOREGROUND,
				Win32WinEvent.EVENT_SYSTEM_FOREGROUND,
				IntPtr.Zero,
				_proc,
				0,
				0,
				Win32WinEvent.WINEVENT_OUTOFCONTEXT);
			_hookInstalled = hook != IntPtr.Zero;
		}
		catch (Exception e)
		{
			_hookInstalled = false;
			_logger.Warning(e, "Failed to install the Windows foreground-focus hook");
		}
		finally
		{
			ready.Set();
		}

		if (!_hookInstalled)
		{
			return;
		}

		if (_stopRequested)
		{
			// The constructor already timed out waiting for this thread, or Dispose already ran, before
			// the hook finished installing. Unhook and leave without ever entering GetMessage - nothing
			// else is going to post a WM_QUIT to stop this loop.
			Win32WinEvent.UnhookWinEvent(hook);
			return;
		}

		try
		{
			while (Win32WinEvent.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
			{
				Win32WinEvent.TranslateMessage(ref msg);
				Win32WinEvent.DispatchMessage(ref msg);
			}
		}
		finally
		{
			Win32WinEvent.UnhookWinEvent(hook);
		}
	}

	private void OnForegroundChanged(IntPtr hook,
		uint eventType,
		IntPtr hwnd,
		int objectId,
		int childId,
		uint threadId,
		uint eventTime)
	{
		try
		{
			if (eventType != Win32WinEvent.EVENT_SYSTEM_FOREGROUND ||
				objectId != Win32WinEvent.OBJID_WINDOW ||
				childId != Win32WinEvent.CHILDID_SELF ||
				hwnd == IntPtr.Zero)
			{
				return;
			}

			if (WindowsFocusedWindowResolver.Resolve(hwnd) is not { } info)
			{
				return;
			}

			lock (_gate)
			{
				_channel.Writer.TryWrite(info);
			}
		}
		catch (Exception e)
		{
			// This callback runs on the pump thread inside the WinEventProc call frame: an exception
			// unwinding into the Win32 message dispatch is undefined behaviour, so every path out of
			// here must be an ordinary return.
			_logger.Error(e, "The Windows focus-change callback failed");
		}
	}

	private static FocusedAppInfo? ReadForegroundWindow()
	{
		var window = Win32Windows.GetForegroundWindow();
		return window == IntPtr.Zero ? null : WindowsFocusedWindowResolver.Resolve(window);
	}
}
