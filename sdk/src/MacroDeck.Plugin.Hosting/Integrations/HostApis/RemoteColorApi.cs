using System.Text.Json;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Colors;
using Serilog;

namespace MacroDeck.Plugin.Hosting.Integrations.HostApis;

// The colors host api: the whole watch table is sent on every change and every connect, and each watch's
// callback fires only when that watch's own value changed.
internal sealed class RemoteColorApi : IColorApi, IDisposable
{
	private static readonly TimeSpan[] _retryDelays =
		[TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(500)];

	private readonly IHostInvoker _invoker;
	private readonly PluginConnectionState _state;
	private readonly HostStateCache _stateCache;
	private readonly ILogger _logger;
	private readonly Lock _lock = new();
	private readonly List<Entry> _entries = [];
	private readonly SemaphoreSlim _syncGate = new(1, 1);

	private long _version;
	private long _syncedVersion = -1;
	private bool _hostHasEntries;
	private object? _unsupportedOn;

	public RemoteColorApi(IHostInvoker invoker,
		PluginConnectionState state,
		HostStateCache stateCache,
		ILogger logger)
	{
		_invoker = invoker;
		_state = state;
		_stateCache = stateCache;
		_logger = logger.ForContext<RemoteColorApi>();
		Lifecycle = new LifecycleView(this);
		_state.Connected += OnConnected;
		_stateCache.ColorsChanged += OnColorsChanged;
	}

	public IColorApi Lifecycle { get; }

	public void Dispose()
	{
		_state.Connected -= OnConnected;
		_stateCache.ColorsChanged -= OnColorsChanged;
		_syncGate.Dispose();
	}

	public async Task<string?> ResolveAsync(string value, string? widgetId = null, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(value);

		var connection = _state.ActiveConnection;
		if (connection is not null && ReferenceEquals(_unsupportedOn, connection))
		{
			return LocalColorApi.Resolve(value);
		}

		try
		{
			var data = await _invoker.InvokeAsync(Protocol.Callbacks.HostApis.Colors,
					HostOperations.Colors.Resolve,
					new ColorsResolveArguments { Value = value, WidgetId = widgetId },
					cancellationToken)
				.ConfigureAwait(false);
			return data?.Deserialize<ColorsResolveResult>(PluginProtocolJson.Options)?.Color;
		}
		catch (HostInvocationException exception) when (IsUnsupported(exception))
		{
			_unsupportedOn = connection;
			return LocalColorApi.Resolve(value);
		}
		catch (HostInvocationException) when (_state.ActiveConnection is null)
		{
			return LocalColorApi.Resolve(value);
		}
	}

	public Task<IAsyncDisposable> WatchAsync(string value,
		Func<string?, CancellationToken, Task> onChanged,
		string? widgetId = null,
		CancellationToken cancellationToken = default)
		=> AddAsync(value, onChanged, widgetId, lifecycle: false, cancellationToken);

	public void ReleaseLifecycleWatches()
	{
		lock (_lock)
		{
			if (_entries.RemoveAll(entry => entry.Lifecycle) > 0)
			{
				_version++;
			}
		}
	}

	public async Task SyncAfterReleaseAsync()
	{
		try
		{
			await TrySyncAsync(CancellationToken.None).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.Debug(exception, "Syncing colour watches failed");
		}
	}

	private async Task<IAsyncDisposable> AddAsync(string value,
		Func<string?, CancellationToken, Task> onChanged,
		string? widgetId,
		bool lifecycle,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(value);
		ArgumentNullException.ThrowIfNull(onChanged);

		var entry = new Entry(this, Guid.NewGuid().ToString("N"), value, widgetId, onChanged, lifecycle);
		lock (_lock)
		{
			if (_entries.Count >= ProtocolLimits.MaxColorWatches)
			{
				throw new InvalidOperationException(
					$"A plugin can watch at most {ProtocolLimits.MaxColorWatches} colours.");
			}

			_entries.Add(entry);
			_version++;
		}

		try
		{
			await TrySyncAsync(cancellationToken).ConfigureAwait(false);
		}
		catch
		{
			lock (_lock)
			{
				if (_entries.Remove(entry))
				{
					_version++;
				}
			}

			throw;
		}

		return new Registration(this, entry);
	}

	private async Task RemoveAsync(Entry entry)
	{
		lock (_lock)
		{
			if (!_entries.Remove(entry))
			{
				return;
			}

			_version++;
		}

		await SyncAfterReleaseAsync().ConfigureAwait(false);
	}

	private void OnConnected(object? sender, PluginConnectedEventArgs e)
	{
		if (!e.Resumed)
		{
			_hostHasEntries = false;
		}

		lock (_lock)
		{
			_syncedVersion = -1;
		}

		_ = SyncAfterReleaseAsync();
	}

	private async Task TrySyncAsync(CancellationToken cancellationToken)
	{
		await _syncGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			ColorsWatchesArguments table;
			long version;
			lock (_lock)
			{
				if (_syncedVersion >= _version)
				{
					return;
				}

				version = _version;
				table = new ColorsWatchesArguments
				{
					Watches =
					[
						.. _entries.Select(entry => new ColorWatchDto
						{
							WatchId = entry.Id,
							Value = entry.Value,
							WidgetId = entry.WidgetId
						})
					]
				};
			}

			var connection = _state.ActiveConnection;
			if (connection is null || (table.Watches.Count == 0 && !_hostHasEntries))
			{
				return;
			}

			if (ReferenceEquals(_unsupportedOn, connection))
			{
				DeliverLocally();
				return;
			}

			for (var attempt = 0;; attempt++)
			{
				try
				{
					await _invoker.InvokeAsync(Protocol.Callbacks.HostApis.Colors,
							HostOperations.Colors.Watches,
							table,
							cancellationToken)
						.ConfigureAwait(false);

					lock (_lock)
					{
						_syncedVersion = version;
					}

					_hostHasEntries = table.Watches.Count > 0;
					return;
				}
				catch (HostInvocationException exception) when (IsUnsupported(exception))
				{
					_unsupportedOn = connection;
					DeliverLocally();
					return;
				}
				catch (HostInvocationException exception) when (exception.Retryable &&
					_state.ActiveConnection is not null &&
					attempt < _retryDelays.Length)
				{
					await Task.Delay(_retryDelays[attempt], cancellationToken).ConfigureAwait(false);
				}
				catch (HostInvocationException exception)
				{
					_logger.Debug("Colour watches stay unsynced until the next change or connect ({Code})", exception.Code);
					ResolveUndelivered();
					return;
				}
			}
		}
		finally
		{
			_syncGate.Release();
		}
	}

	// Without the colors host api a watch still gets its one value: a fixed colour, or no colour.
	private void DeliverLocally()
	{
		Entry[] entries;
		lock (_lock)
		{
			entries = [.. _entries];
		}

		foreach (var entry in entries)
		{
			_ = entry.DeliverAsync(LocalColorApi.Resolve(entry.Value), _logger);
		}
	}

	// A watch is promised its first value even when the host would not take the table this time.
	private void ResolveUndelivered()
	{
		Entry[] entries;
		lock (_lock)
		{
			entries = [.. _entries.Where(entry => !entry.Delivered)];
		}

		foreach (var entry in entries)
		{
			_ = DeliverResolvedAsync(entry);
		}
	}

	private async Task DeliverResolvedAsync(Entry entry)
	{
		string? color;
		try
		{
			color = await ResolveAsync(entry.Value, entry.WidgetId).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.Debug(exception, "Resolving a watched colour failed");
			color = LocalColorApi.Resolve(entry.Value);
		}

		await entry.DeliverAsync(color, _logger).ConfigureAwait(false);
	}

	private void OnColorsChanged()
	{
		var state = _stateCache.Get<ColorWatchStateDto>(Protocol.Callbacks.HostApis.Colors);
		if (state is null)
		{
			return;
		}

		Dictionary<string, Entry> entries;
		lock (_lock)
		{
			entries = _entries.ToDictionary(entry => entry.Id, StringComparer.Ordinal);
		}

		foreach (var value in state.Values)
		{
			if (entries.TryGetValue(value.WatchId, out var entry))
			{
				_ = entry.DeliverAsync(value.Color, _logger);
			}
		}
	}

	private bool IsActive(Entry entry)
	{
		lock (_lock)
		{
			return _entries.Contains(entry);
		}
	}

	private static bool IsUnsupported(HostInvocationException exception)
		=> string.Equals(exception.Code, ProtocolErrorCodes.CapabilityUnsupported, StringComparison.Ordinal);

	private sealed class Entry(RemoteColorApi owner, string id, string value, string? widgetId,
		Func<string?, CancellationToken, Task> onChanged, bool lifecycle)
	{
		private readonly Lock _chain = new();
		private Task _tail = Task.CompletedTask;
		private bool _delivered;
		private string? _last;

		public string Id { get; } = id;

		public string Value { get; } = value;

		public string? WidgetId { get; } = widgetId;

		public bool Lifecycle { get; } = lifecycle;

		public bool Delivered => Volatile.Read(ref _delivered);

		// Chained so deliveries run one at a time per watch, off the caller's thread, in arrival order.
		public Task DeliverAsync(string? color, ILogger logger)
		{
			lock (_chain)
			{
				_tail = DeliverAfterAsync(_tail, color, logger);
				return _tail;
			}
		}

		private async Task DeliverAfterAsync(Task previous, string? color, ILogger logger)
		{
			await previous.ConfigureAwait(false);
			await Task.Yield();
			try
			{
				if (!owner.IsActive(this) ||
					(_delivered && string.Equals(color, _last, StringComparison.Ordinal)))
				{
					return;
				}

				_delivered = true;
				_last = color;
				await onChanged(color, CancellationToken.None).ConfigureAwait(false);
			}
			catch (Exception exception) when (exception is not OutOfMemoryException)
			{
				logger.Warning(exception, "A colour watch callback failed");
			}
		}
	}

	private sealed class Registration(RemoteColorApi api, Entry entry) : IAsyncDisposable
	{
		private int _disposed;

		public async ValueTask DisposeAsync()
		{
			if (Interlocked.Exchange(ref _disposed, 1) == 0)
			{
				await api.RemoveAsync(entry).ConfigureAwait(false);
			}
		}
	}

	private sealed class LifecycleView(RemoteColorApi api) : IColorApi
	{
		public Task<string?> ResolveAsync(string value, string? widgetId = null, CancellationToken cancellationToken = default)
			=> api.ResolveAsync(value, widgetId, cancellationToken);

		public Task<IAsyncDisposable> WatchAsync(string value,
			Func<string?, CancellationToken, Task> onChanged,
			string? widgetId = null,
			CancellationToken cancellationToken = default)
			=> api.AddAsync(value, onChanged, widgetId, lifecycle: true, cancellationToken);
	}
}
