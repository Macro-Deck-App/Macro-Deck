using System.Diagnostics;
using System.Runtime.Versioning;

namespace MacroDeckHost.Integrations.Clipboard.Native;

[SupportedOSPlatform("linux")]
internal sealed class WaylandClipboardChangeSource : IClipboardChangeSource
{
	internal static readonly TimeSpan StartupWindow = TimeSpan.FromSeconds(2);

	private static readonly TimeSpan _firstBackoff = TimeSpan.FromSeconds(1);
	private static readonly TimeSpan _maxBackoff = TimeSpan.FromSeconds(30);

	private readonly Func<Process> _start;
	private readonly CancellationTokenSource _stop = new();
	private readonly Lock _gate = new();

	private Task? _loop;
	private Process? _process;
	private bool _unsupported;
	private bool _disposed;

	public WaylandClipboardChangeSource()
		: this(() => ClipboardProcess.StartStreaming(LinuxClipboardCommands.Watch()))
	{
	}

	internal WaylandClipboardChangeSource(Func<Process> start)
	{
		_start = start;
	}

	public event Action<long?>? Changed;

	public event Action? WatchUnsupported;

	public bool IsWatchUnsupported => Volatile.Read(ref _unsupported);

	public void Start()
	{
		lock (_gate)
		{
			if (_disposed || _loop is not null)
			{
				return;
			}

			_loop = Task.Run(() => WatchAsync(_stop.Token));
		}
	}

	public void Dispose()
	{
		Task? loop;
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			loop = _loop;
			_stop.Cancel();
			if (_process is { } process)
			{
				ClipboardProcess.Kill(process);
			}
		}

		try
		{
			_ = loop?.Wait(StartupWindow);
		}
		catch (AggregateException)
		{
		}

		_stop.Dispose();
	}

	internal static TimeSpan NextBackoff(TimeSpan current)
	{
		var doubled = current + current;
		return doubled > _maxBackoff ? _maxBackoff : doubled;
	}

	private async Task WatchAsync(CancellationToken cancellationToken)
	{
		var backoff = _firstBackoff;
		var first = true;
		while (!cancellationToken.IsCancellationRequested)
		{
			var started = Stopwatch.StartNew();
			var ran = await RunOnceAsync(cancellationToken).ConfigureAwait(false);
			if (cancellationToken.IsCancellationRequested)
			{
				return;
			}

			if (first && (!ran || started.Elapsed < StartupWindow))
			{
				// No data-control protocol (GNOME, weston) makes wl-paste --watch exit at once; retrying cannot help.
				Volatile.Write(ref _unsupported, true);
				WatchUnsupported?.Invoke();
				return;
			}

			first = false;
			backoff = started.Elapsed > _maxBackoff ? _firstBackoff : backoff;
			try
			{
				await Task.Delay(backoff, cancellationToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				return;
			}

			backoff = NextBackoff(backoff);
		}
	}

	private async Task<bool> RunOnceAsync(CancellationToken cancellationToken)
	{
		Process process;
		try
		{
			process = _start();
		}
		catch (ClipboardException)
		{
			return false;
		}

		lock (_gate)
		{
			if (_disposed)
			{
				ClipboardProcess.Kill(process);
				process.Dispose();
				return true;
			}

			_process = process;
		}

		try
		{
			var errors = process.StandardError.BaseStream.CopyToAsync(Stream.Null, cancellationToken);
			// Every wl-paste run executes its command once for the selection it finds at start, which is no change.
			var skipFirst = true;
			while (await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false) is not null)
			{
				if (skipFirst)
				{
					skipFirst = false;
					continue;
				}

				Changed?.Invoke(null);
			}

			await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
			await errors.ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is OperationCanceledException
			or IOException
			or ObjectDisposedException)
		{
		}
		finally
		{
			lock (_gate)
			{
				_process = null;
			}

			ClipboardProcess.Kill(process);
			process.Dispose();
		}

		return true;
	}
}
