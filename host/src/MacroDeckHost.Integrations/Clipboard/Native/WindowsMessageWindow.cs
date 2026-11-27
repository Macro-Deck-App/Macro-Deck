using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static MacroDeckHost.Integrations.Clipboard.Native.WindowsClipboardNative;

namespace MacroDeckHost.Integrations.Clipboard.Native;

// Win32 lets only the creating thread use and destroy a window, and a clipboard owner must keep
// pumping or other applications block in EmptyClipboard, so one thread owns the window and its loop.
[SupportedOSPlatform("windows")]
internal sealed class WindowsMessageWindow : IDisposable
{
	private const uint WmRunQueue = WmApp + 1;
	private const uint WmShutdown = WmApp + 2;

	private static readonly TimeSpan _joinTimeout = TimeSpan.FromSeconds(2);

	private readonly ConcurrentQueue<(Action<IntPtr> Run, Action Fail)> _work = new();
	private readonly TaskCompletionSource<IntPtr> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly Func<uint, bool>? _handler;
	private readonly Func<IntPtr, bool>? _onCreated;
	private readonly Action<IntPtr>? _onDestroying;
	private readonly WindowProcedure _procedure;
	private readonly string _className = "MacroDeckClipboard" + Guid.NewGuid().ToString("N");
	private readonly Thread _thread;

	private IntPtr _window;
	private int _disposed;

	public WindowsMessageWindow(
		Func<uint, bool>? handler = null,
		Func<IntPtr, bool>? onCreated = null,
		Action<IntPtr>? onDestroying = null)
	{
		_handler = handler;
		_onCreated = onCreated;
		_onDestroying = onDestroying;
		_procedure = Procedure;
		_thread = new Thread(Run) { IsBackground = true, Name = "Macro Deck clipboard window" };
		_thread.SetApartmentState(ApartmentState.STA);
		_thread.Start();
	}

	public Task<IntPtr> Ready => _ready.Task;

	public async Task<T> InvokeAsync<T>(Func<IntPtr, T> work)
	{
		var window = await _ready.Task.ConfigureAwait(false);
		var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
		_work.Enqueue((hwnd =>
			{
				try
				{
					completion.TrySetResult(work(hwnd));
				}
				catch (Exception exception)
				{
					completion.TrySetException(exception);
				}
			},
			() => completion.TrySetException(new ClipboardException(ClipboardFailure.Unavailable))));

		if (Volatile.Read(ref _disposed) == 1 || !PostMessageW(window, WmRunQueue, IntPtr.Zero, IntPtr.Zero))
		{
			throw new ClipboardException(ClipboardFailure.Unavailable);
		}

		return await completion.Task.ConfigureAwait(false);
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) == 1)
		{
			return;
		}

		if (_ready.Task is { IsCompletedSuccessfully: true, Result: var window } && window != IntPtr.Zero)
		{
			_ = PostMessageW(window, WmShutdown, IntPtr.Zero, IntPtr.Zero);
		}

		if (Thread.CurrentThread != _thread)
		{
			_ = _thread.Join(_joinTimeout);
		}
	}

	private void Run()
	{
		var instance = GetModuleHandleW(null);
		var windowClass = new WndClassEx
		{
			CbSize = (uint)Marshal.SizeOf<WndClassEx>(),
			LpfnWndProc = Marshal.GetFunctionPointerForDelegate(_procedure),
			HInstance = instance,
			LpszClassName = _className
		};

		if (RegisterClassExW(ref windowClass) == 0)
		{
			_ready.TrySetException(new ClipboardException(ClipboardFailure.Unavailable));
			return;
		}

		try
		{
			_window = CreateWindowExW(0,
				_className,
				string.Empty,
				0,
				0,
				0,
				0,
				0,
				HwndMessage,
				IntPtr.Zero,
				instance,
				IntPtr.Zero);
			if (_window == IntPtr.Zero || _onCreated is not null && !_onCreated(_window))
			{
				if (_window != IntPtr.Zero)
				{
					_ = DestroyWindow(_window);
				}

				_ready.TrySetException(new ClipboardException(ClipboardFailure.Unavailable));
				return;
			}

			_ready.TrySetResult(_window);
			if (Volatile.Read(ref _disposed) == 0)
			{
				Pump();
			}
			else
			{
				Destroy();
			}
		}
		finally
		{
			_ = UnregisterClassW(_className, instance);
			FailPendingWork();
			GC.KeepAlive(_procedure);
		}
	}

	private void Pump()
	{
		while (GetMessageW(out var message, IntPtr.Zero, 0, 0) > 0)
		{
			if (message.Hwnd == _window && message.Message == WmRunQueue)
			{
				Drain();
				continue;
			}

			if (message.Hwnd == _window && message.Message == WmShutdown)
			{
				Drain();
				Destroy();
				PostQuitMessage(0);
				continue;
			}

			_ = DispatchMessageW(ref message);
		}
	}

	private void Destroy()
	{
		_onDestroying?.Invoke(_window);
		_ = DestroyWindow(_window);
	}

	private void Drain()
	{
		while (_work.TryDequeue(out var work))
		{
			work.Run(_window);
		}
	}

	private void FailPendingWork()
	{
		while (_work.TryDequeue(out var work))
		{
			work.Fail();
		}
	}

	private IntPtr Procedure(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
	{
		// An exception escaping a window procedure tears down the process, so a handler fault is dropped.
		try
		{
			if (_handler is not null && _handler(message))
			{
				return IntPtr.Zero;
			}
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			return IntPtr.Zero;
		}

		return DefWindowProcW(hwnd, message, wParam, lParam);
	}
}
