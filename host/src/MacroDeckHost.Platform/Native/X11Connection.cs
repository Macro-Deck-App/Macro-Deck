using System.Runtime.Versioning;

namespace MacroDeckHost.Platform.Native;

[SupportedOSPlatform("linux")]
internal sealed class X11Connection : IDisposable
{
	public const int AnyGeneration = -1;

	private static readonly TimeSpan _retryInterval = TimeSpan.FromSeconds(1);

	// Xlib keeps a lost display locked for every thread but the one that hit the loss, so all calls
	// on a display are serialized here and a lost display is never called into again.
	private readonly global::System.Threading.Lock _lock = new();
	private readonly Func<IntPtr, bool> _attach;
	private readonly Func<Exception> _unavailable;
	private readonly string? _displayName;
	private IntPtr _display;
	private int _generation;
	private long _nextAttempt;
	private bool _libraryMissing;
	private bool _disposed;

	public X11Connection(Func<IntPtr, bool> attach, Func<Exception> unavailable, string? displayName = null)
	{
		_attach = attach;
		_unavailable = unavailable;
		_displayName = displayName;
		lock (_lock)
		{
			_ = EnsureConnected();
		}
	}

	public bool IsAvailable
	{
		get
		{
			var display = Volatile.Read(ref _display);
			if (display != IntPtr.Zero)
			{
				return !X11Windows.IsLost(display);
			}

			if (!_lock.TryEnter())
			{
				return false;
			}

			try
			{
				return EnsureConnected();
			}
			finally
			{
				_lock.Exit();
			}
		}
	}

	public int Generation => Volatile.Read(ref _generation);

	public void Run(Action<IntPtr> action, int generation = AnyGeneration)
	{
		_ = Run(display =>
			{
				action(display);
				return true;
			},
			generation);
	}

	public T Run<T>(Func<IntPtr, T> action, int generation = AnyGeneration)
	{
		lock (_lock)
		{
			if (!EnsureConnected() || !IsGeneration(generation))
			{
				throw _unavailable();
			}

			return Invoke(action);
		}
	}

	public T RunIfConnected<T>(Func<IntPtr, T> action, T fallback)
	{
		lock (_lock)
		{
			return EnsureConnected() ? Invoke(action) : fallback;
		}
	}

	public bool TryRun(Action<IntPtr> action, int generation = AnyGeneration)
	{
		lock (_lock)
		{
			if (!EnsureConnected() || !IsGeneration(generation))
			{
				return false;
			}

			try
			{
				action(_display);
			}
			finally
			{
				_ = DropIfLost();
			}

			return _display != IntPtr.Zero;
		}
	}

	public void Dispose()
	{
		lock (_lock)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			if (_display != IntPtr.Zero)
			{
				X11Windows.CloseDisplay(_display);
				Volatile.Write(ref _display, IntPtr.Zero);
			}
		}
	}

	private T Invoke<T>(Func<IntPtr, T> action)
	{
		T result;
		try
		{
			result = action(_display);
		}
		finally
		{
			_ = DropIfLost();
		}

		return _display != IntPtr.Zero ? result : throw _unavailable();
	}

	private bool IsGeneration(int generation)
	{
		return generation == AnyGeneration || generation == _generation;
	}

	private bool EnsureConnected()
	{
		if (_display != IntPtr.Zero)
		{
			return true;
		}

		if (_disposed || _libraryMissing || Environment.TickCount64 < _nextAttempt)
		{
			return false;
		}

		if (Connect())
		{
			return true;
		}

		_nextAttempt = Environment.TickCount64 + (long)_retryInterval.TotalMilliseconds;
		return false;
	}

	private bool Connect()
	{
		if (!OperatingSystem.IsLinux())
		{
			return false;
		}

		IntPtr display;
		try
		{
			display = X11Windows.OpenDisplay(_displayName);
		}
		catch (DllNotFoundException)
		{
			_libraryMissing = true;
			return false;
		}

		if (display == IntPtr.Zero)
		{
			return false;
		}

		bool attached;
		try
		{
			attached = _attach(display);
		}
		catch (DllNotFoundException)
		{
			attached = false;
		}

		if (X11Windows.IsLost(display))
		{
			X11Windows.Forget(display);
			return false;
		}

		if (!attached)
		{
			X11Windows.CloseDisplay(display);
			return false;
		}

		Volatile.Write(ref _generation, _generation + 1);
		Volatile.Write(ref _display, display);
		return true;
	}

	// XCloseDisplay on a lost display crashes inside Xlib, so a lost display is left open: its
	// memory and socket leak once per server loss.
	private bool DropIfLost()
	{
		if (_display == IntPtr.Zero || !X11Windows.IsLost(_display))
		{
			return false;
		}

		X11Windows.Forget(_display);
		Volatile.Write(ref _display, IntPtr.Zero);
		return true;
	}
}
