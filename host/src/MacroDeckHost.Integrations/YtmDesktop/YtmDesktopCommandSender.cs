using MacroDeck.Sdk.Logging;
using Serilog;

namespace MacroDeckHost.Integrations.YtmDesktop;

internal sealed class YtmDesktopCommandSender : IDisposable
{
	private static readonly ILogger _logger =
		IntegrationLog.For<YtmDesktopCommandSender>(YtmDesktopIntegration.IntegrationId);

	private readonly Func<string, object?, CancellationToken, Task> _send;
	private readonly TimeSpan _minimumInterval;
	private readonly CancellationTokenSource _cts = new();
	private readonly Lock _gate = new();
	private readonly Dictionary<string, object?> _pending = new(StringComparer.Ordinal);
	private readonly List<string> _order = [];

	private bool _draining;
	private bool _disposed;

	public YtmDesktopCommandSender(
		Func<string, object?, CancellationToken, Task> send,
		TimeSpan? minimumInterval = null)
	{
		_send = send;
		_minimumInterval = minimumInterval ?? TimeSpan.FromMilliseconds(500);
	}

	public void Enqueue(string command, object? data)
	{
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			if (!_pending.ContainsKey(command))
			{
				_order.Add(command);
			}

			_pending[command] = data;

			if (_draining)
			{
				return;
			}

			_draining = true;
		}

		_ = Task.Run(() => DrainAsync(_cts.Token), CancellationToken.None);
	}

	public async Task SendAsync(string command, object? data, CancellationToken cancellationToken)
		=> await _send(command, data, cancellationToken).ConfigureAwait(false);

	public void Dispose()
	{
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			_pending.Clear();
			_order.Clear();
		}

		_cts.Cancel();
		_cts.Dispose();
	}

	private async Task DrainAsync(CancellationToken cancellationToken)
	{
		while (true)
		{
			string command;
			object? data;

			lock (_gate)
			{
				if (_order.Count == 0 || _disposed || cancellationToken.IsCancellationRequested)
				{
					_draining = false;
					return;
				}

				command = _order[0];
				_order.RemoveAt(0);
				data = _pending[command];
				_pending.Remove(command);
			}

			try
			{
				await _send(command, data, cancellationToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				lock (_gate)
				{
					_draining = false;
				}

				return;
			}
			catch (Exception ex)
			{
				// One rejected command must not stop the queue; the next slider write still has to land.
				_logger.Debug(ex, "Queued {Command} was not accepted", command);
			}

			try
			{
				await Task.Delay(_minimumInterval, cancellationToken).ConfigureAwait(false);
			}
			catch (Exception)
			{
				lock (_gate)
				{
					_draining = false;
				}

				return;
			}
		}
	}
}
