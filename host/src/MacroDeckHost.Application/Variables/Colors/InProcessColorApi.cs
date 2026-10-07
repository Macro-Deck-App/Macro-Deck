using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Sdk.Colors;
using Serilog;

namespace MacroDeckHost.Application.Variables.Colors;

// The colors api of one in-process integration's current initialization; disposing it releases every
// watch it made, which is what a shutdown or a new initialization does.
public sealed class InProcessColorApi : IColorApi, IAsyncDisposable
{
	private readonly PluginColorWatches _watches;
	private readonly ILogger _logger;
	private readonly Lock _gate = new();
	private readonly List<Watch> _active = [];
	private bool _disposed;

	public InProcessColorApi(PluginColorWatches watches, ILogger logger)
	{
		_watches = watches;
		_logger = logger.ForContext<InProcessColorApi>();
		_watches.Signal.Changed += OnChanged;
	}

	public Task<string?> ResolveAsync(string value, string? widgetId = null, CancellationToken cancellationToken = default)
		=> Task.FromResult(_watches.Resolve(value, widgetId));

	public Task<IAsyncDisposable> WatchAsync(string value,
		Func<string?, CancellationToken, Task> onChanged,
		string? widgetId = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(value);
		ArgumentNullException.ThrowIfNull(onChanged);

		var watch = new Watch(this, value, widgetId, onChanged);
		lock (_gate)
		{
			ObjectDisposedException.ThrowIf(_disposed, this);
			if (_active.Count >= ProtocolLimits.MaxColorWatches)
			{
				throw new InvalidOperationException(
					$"An integration can watch at most {ProtocolLimits.MaxColorWatches} colours.");
			}

			_active.Add(watch);
		}

		_ = watch.RefreshAsync(first: true);
		return Task.FromResult<IAsyncDisposable>(watch);
	}

	public ValueTask DisposeAsync()
	{
		lock (_gate)
		{
			_disposed = true;
			_active.Clear();
		}

		_watches.Signal.Changed -= OnChanged;
		return ValueTask.CompletedTask;
	}

	private void OnChanged()
	{
		Watch[] active;
		lock (_gate)
		{
			active = [.. _active];
		}

		foreach (var watch in active)
		{
			_ = watch.RefreshAsync(first: false);
		}
	}

	private bool IsActive(Watch watch)
	{
		lock (_gate)
		{
			return _active.Contains(watch);
		}
	}

	private void Remove(Watch watch)
	{
		lock (_gate)
		{
			_active.Remove(watch);
		}
	}

	private sealed class Watch(InProcessColorApi owner, string value, string? widgetId,
		Func<string?, CancellationToken, Task> onChanged) : IAsyncDisposable
	{
		private readonly Lock _chain = new();
		private Task _tail = Task.CompletedTask;
		private bool _delivered;
		private string? _last;

		// Chained so refreshes run one at a time per watch, off the notifying thread, so a slow callback
		// delays only its own watch and a value is never delivered twice.
		public Task RefreshAsync(bool first)
		{
			lock (_chain)
			{
				_tail = RefreshAfterAsync(_tail, first);
				return _tail;
			}
		}

		private async Task RefreshAfterAsync(Task previous, bool first)
		{
			await previous.ConfigureAwait(false);
			await Task.Yield();
			try
			{
				if (!owner.IsActive(this) || (first && _delivered))
				{
					return;
				}

				var current = owner._watches.Resolve(value, widgetId);
				if (_delivered && string.Equals(current, _last, StringComparison.Ordinal))
				{
					return;
				}

				_delivered = true;
				_last = current;
				await onChanged(current, CancellationToken.None).ConfigureAwait(false);
			}
			catch (Exception exception) when (exception is not OutOfMemoryException)
			{
				owner._logger.Warning(exception, "A colour watch callback failed");
			}
		}

		public ValueTask DisposeAsync()
		{
			owner.Remove(this);
			return ValueTask.CompletedTask;
		}
	}
}
