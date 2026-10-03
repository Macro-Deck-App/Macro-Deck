using System.Diagnostics;
using Serilog;

namespace MacroDeckHost.Integrations.System.Volume;

internal sealed class PactlEventMonitor : IDisposable
{
	private static readonly ILogger _logger = Log.ForContext<PactlEventMonitor>();

	private readonly string _fileName;
	private readonly IReadOnlyList<string> _arguments;
	private readonly IReadOnlyDictionary<string, string?> _environment;
	private readonly Action _onEvent;
	private readonly TimeSpan _coalesce;
	private readonly TimeSpan _minBackoff;
	private readonly TimeSpan _maxBackoff;
	private readonly CancellationTokenSource _cancellation = new();
	private readonly object _gate = new();

	private bool _pending;
	private Task? _loop;

	public PactlEventMonitor(
		Action onEvent,
		IReadOnlyDictionary<string, string?> environment,
		string fileName = "pactl",
		IReadOnlyList<string>? arguments = null,
		TimeSpan? coalesce = null,
		TimeSpan? minBackoff = null,
		TimeSpan? maxBackoff = null)
	{
		_onEvent = onEvent;
		_environment = environment;
		_fileName = fileName;
		_arguments = arguments ?? ["subscribe"];
		_coalesce = coalesce ?? TimeSpan.FromMilliseconds(75);
		_minBackoff = minBackoff ?? TimeSpan.FromSeconds(1);
		_maxBackoff = maxBackoff ?? TimeSpan.FromSeconds(30);
	}

	public void Start() => _loop ??= Task.Run(RunAsync);

	public void Dispose()
	{
		_cancellation.Cancel();
		try
		{
			_loop?.Wait(TimeSpan.FromSeconds(2));
		}
		catch (AggregateException)
		{
		}
	}

	private async Task RunAsync()
	{
		var token = _cancellation.Token;
		var backoff = _minBackoff;
		while (!token.IsCancellationRequested)
		{
			var startedAt = Environment.TickCount64;
			try
			{
				await RunOnceAsync(token);
			}
			catch (OperationCanceledException)
			{
				return;
			}
			catch (Exception ex) when (ex is InvalidOperationException or global::System.ComponentModel.Win32Exception
				or IOException)
			{
				_logger.Debug(ex, "The audio event subscription failed");
			}

			var delay = Environment.TickCount64 - startedAt > _maxBackoff.TotalMilliseconds ? _minBackoff : backoff;
			backoff = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, _maxBackoff.Ticks));
			try
			{
				await Task.Delay(delay, token);
			}
			catch (OperationCanceledException)
			{
				return;
			}
		}
	}

	private async Task RunOnceAsync(CancellationToken token)
	{
		using var process = ProcessRunner.StartStreaming(_fileName, _arguments, _environment);
		using var registration = token.Register(() => Kill(process));
		_ = process.StandardError.ReadToEndAsync(CancellationToken.None);

		Notify();
		while (await process.StandardOutput.ReadLineAsync(token) is { } line)
		{
			if (PactlOutputParser.IsAudioEvent(line))
			{
				Notify();
			}
		}

		await process.WaitForExitAsync(token);
	}

	private void Notify()
	{
		lock (_gate)
		{
			if (_pending)
			{
				return;
			}

			_pending = true;
		}

		_ = Task.Run(async () =>
		{
			try
			{
				await Task.Delay(_coalesce, _cancellation.Token);
			}
			catch (OperationCanceledException)
			{
				return;
			}

			lock (_gate)
			{
				_pending = false;
			}

			_onEvent();
		});
	}

	private static void Kill(Process process)
	{
		try
		{
			process.Kill(entireProcessTree: true);
		}
		catch (Exception ex) when (ex is InvalidOperationException or global::System.ComponentModel.Win32Exception)
		{
		}
	}
}
